#include "DrawLayer.h"

#include "Base64.h"
#include "Json.h"

#include <windows.h>

#include <d3d9.h>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <cwctype>
#include <exception>

namespace hud {

namespace hooks {
namespace {
std::atomic<DrawLayer*> gActive{nullptr};
void* gPresentOriginal = nullptr;
void* gResetOriginal = nullptr;
using PresentFn = HRESULT(__stdcall*)(IDirect3DDevice9*, const RECT*, const RECT*, HWND, const RGNDATA*);
using ResetFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRESENT_PARAMETERS*);
} // namespace

void SetActive(DrawLayer* layer) { gActive.store(layer); }
void*& PresentOriginal() { return gPresentOriginal; }
void*& ResetOriginal() { return gResetOriginal; }

long __stdcall Present(IDirect3DDevice9* self, const void* source, const void* dest, void* window, const void* dirty) {
    DrawLayer* layer = gActive.load();
    if (layer != nullptr) layer->OnPresent(self);
    return static_cast<PresentFn>(gPresentOriginal)(self, static_cast<const RECT*>(source), static_cast<const RECT*>(dest),
                                                    static_cast<HWND>(window), static_cast<const RGNDATA*>(dirty));
}

long __stdcall Reset(IDirect3DDevice9* self, void* parameters) {
    DrawLayer* layer = gActive.load();
    const bool handled = layer != nullptr && layer->OnBeforeReset(self);   // "device lost": default-pool objects are released
    const HRESULT hr = static_cast<ResetFn>(gResetOriginal)(self, static_cast<D3DPRESENT_PARAMETERS*>(parameters));
    if (layer != nullptr) layer->OnAfterReset(handled, hr);
    return hr;
}
} // namespace hooks

namespace {

constexpr size_t kMaxOperationBytes = 64 * 1024;
constexpr size_t kMaxImageOperationBytes = 6u << 20;   // 4 MiB of pixels in base64 plus the envelope

std::string Num(uint64_t v) { return std::to_string(v); }

constexpr size_t kMaxMeasureBytes = 2048;
constexpr int kMeasureOversample = 4;   // advances are taken at 4x and divided back, so widths keep their fraction

std::string FormatWidth(double w) {
    char text[40];
    sprintf_s(text, "%.3f", w);
    return text;
}

int Respond(const std::string& json, char* buffer, uint32_t capacity, uint32_t* length) {
    *length = static_cast<uint32_t>(json.size());
    if (buffer == nullptr || capacity < json.size() + 1) return OMSILAUNCH_BRIDGE_BUFFER_TOO_SMALL;
    std::memcpy(buffer, json.data(), json.size());
    buffer[json.size()] = 0;
    return 0;
}

std::string ErrorJson(const char* code, const std::string& detail) {
    std::string out = "{\"ok\":false,\"code\":";
    json::AppendQuoted(out, code);
    out += ",\"detail\":";
    json::AppendQuoted(out, detail);
    out += "}";
    return out;
}

bool WideFromUtf8(const std::string& utf8, std::wstring& out) {
    out.clear();
    if (utf8.empty()) return false;
    const int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, utf8.data(), static_cast<int>(utf8.size()), nullptr, 0);
    if (n <= 0) return false;
    out.resize(static_cast<size_t>(n));
    return MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, utf8.data(), static_cast<int>(utf8.size()), &out[0], n) == n;
}

bool IsAbsolutePath(const std::wstring& p) {
    if (p.size() >= 3 && ((p[0] >= L'A' && p[0] <= L'Z') || (p[0] >= L'a' && p[0] <= L'z')) && p[1] == L':' && (p[2] == L'\\' || p[2] == L'/')) return true;
    return p.size() >= 3 && p[0] == L'\\' && p[1] == L'\\';
}

bool HasFontExtension(const std::wstring& p) {
    if (p.size() < 5) return false;
    std::wstring ext = p.substr(p.size() - 4);
    for (wchar_t& c : ext) c = static_cast<wchar_t>(towlower(c));
    return ext == L".ttf" || ext == L".otf";
}

bool RoleFromName(const std::string& s, FontRole& role) {
    if (s == "regular") role = FontRole::Regular;
    else if (s == "medium") role = FontRole::Medium;
    else if (s == "bold") role = FontRole::Bold;
    else if (s == "black") role = FontRole::Black;
    else if (s == "condensed") role = FontRole::Condensed;
    else return false;
    return true;
}

// A JSON number that is an integer in [lo, hi].
bool IntMember(const json::Value& o, const char* key, long long lo, long long hi, long long& out) {
    const json::Value* v = o.Find(key);
    if (v == nullptr || v->type != json::Type::Number) return false;
    const double d = v->number;
    if (d != static_cast<double>(static_cast<long long>(d)) || d < static_cast<double>(lo) || d > static_cast<double>(hi)) return false;
    out = static_cast<long long>(d);
    return true;
}

bool StringMember(const json::Value& o, const char* key, std::string& out) {
    const json::Value* v = o.Find(key);
    if (v == nullptr || v->type != json::Type::String) return false;
    out = v->text;
    return true;
}

// Runs the drawing of one frame; an access violation inside the driver or the
// game's state disables drawing instead of taking OMSI down.
bool GuardedCall(DrawLayer* layer, void (DrawLayer::*fn)(IDirect3DDevice9*), IDirect3DDevice9* device, unsigned long& code) {
    __try {
        (layer->*fn)(device);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        code = GetExceptionCode();
        return false;
    }
}

} // namespace

DrawLayer::DrawLayer() : glyphs_(fonts_), renderer_(glyphs_) {}

DrawLayer::~DrawLayer() = default;

std::string DrawLayer::LastError() const {
    std::lock_guard<std::mutex> guard(errorLock_);
    return lastError_;
}

void DrawLayer::Emit(const char* name, const std::string& json) noexcept {
    try {
        if (bridge_ != nullptr && bridge_->emit != nullptr && !stopped_.load()) bridge_->emit(bridge_->context, name, json.c_str());
    } catch (...) {
    }
}

void DrawLayer::Fail(const char* code, const std::string& detail) noexcept {
    try {
        failed_.store(true);
        {
            std::lock_guard<std::mutex> guard(errorLock_);
            lastError_ = std::string(code) + ": " + detail;
        }
        std::string data = "{\"code\":";
        json::AppendQuoted(data, code);
        data += ",\"detail\":";
        json::AppendQuoted(data, detail);
        data += "}";
        Emit("draw_failed", data);
    } catch (...) {
    }
}

bool DrawLayer::IsGameDevice(IDirect3DDevice9* device) const {
    return bridge_ != nullptr && bridge_->d3d_is_game_device != nullptr && bridge_->d3d_is_game_device(bridge_->context, device) == 1;
}

int DrawLayer::RequestHooks() {
    int worst = 0;
    if (!resetHooked_) {
        const int rc = bridge_->request_hook(bridge_->context, "d3d9:device.Reset", reinterpret_cast<void*>(&hooks::Reset), &hooks::ResetOriginal());
        if (rc == 0) resetHooked_ = true;
        else worst = rc;
    }
    if (!presentHooked_) {
        const int rc = bridge_->request_hook(bridge_->context, "d3d9:device.Present", reinterpret_cast<void*>(&hooks::Present), &hooks::PresentOriginal());
        if (rc == 0) presentHooked_ = true;
        else if (worst == 0 || worst == 10) worst = rc;
    }
    return worst;
}

int DrawLayer::Start(const omsilaunch_bridge_v1* bridge) {
    if (bridge == nullptr || bridge->version != OMSILAUNCH_MODULE_NATIVE_V1 || bridge->request_hook == nullptr || bridge->emit == nullptr) return 1;
    // Drawing only on the game's device needs the protocol 3 table.
    if (bridge->size < OMSILAUNCH_BRIDGE_V1_SIZE_WITH_D3D_IS_GAME_DEVICE || bridge->d3d_is_game_device == nullptr) return 1;
    bridge_ = bridge;
    stopped_.store(false);
    hooks::SetActive(this);
    // The device usually does not exist yet (10): the hooks are then requested in gameplay_ready.
    const int rc = RequestHooks();
    if (rc != 0 && rc != 10) {
        hooks::SetActive(nullptr);
        return rc;
    }
    return 0;
}

int DrawLayer::GameplayReady(const omsilaunch_bridge_v1* bridge) {
    if (bridge != nullptr) bridge_ = bridge;
    if (bridge_ == nullptr || stopped_.load()) return 1;
    const int rc = RequestHooks();
    if (rc != 0) return rc;
    std::string data = "{\"present\":\"installed\",\"reset\":\"installed\"}";
    Emit("hooks", data);
    return 0;
}

void DrawLayer::Stop() {
    stopped_.store(true);          // detours become pass-through at once
    hooks::SetActive(nullptr);
    visible_.store(false);
    {
        std::lock_guard<std::mutex> guard(deviceLock_);
        try {
            renderer_.SetScene(nullptr);
            renderer_.ReleaseBackendResources(backend_);
            backend_.Detach();
            fonts_.Clear();
        } catch (...) {
        }
    }
}

void DrawLayer::OnPresent(IDirect3DDevice9* device) noexcept {
    if (stopped_.load() || device == nullptr) return;
    try {
        if (!IsGameDevice(device)) return;
        // Never wait for long inside Present: a concurrent Reset or Stop owns the lock, skip this frame.
        std::unique_lock<std::mutex> guard(deviceLock_, std::try_to_lock);
        if (!guard.owns_lock() || stopped_.load()) return;
        unsigned long code = 0;
        if (!GuardedCall(this, &DrawLayer::DrawFrame, device, code)) {
            char text[48];
            sprintf_s(text, "exception 0x%08lX while drawing", code);
            Fail("access_violation", text);
        }
    } catch (const std::exception& e) {
        Fail("exception", e.what());
    } catch (...) {
        Fail("exception", "unknown exception while drawing");
    }
}

void DrawLayer::DrawFrame(IDirect3DDevice9* device) {
    if (backend_.Device() != device) {
        renderer_.ForgetBackendResources();   // textures belonged to the previous device
        backend_.Attach(device);
    }
    if (releaseBackground_.exchange(false)) backend_.ReleaseBackground();
    if (capturePending_.exchange(false)) {
        // Before anything of ours is drawn: the back buffer still holds the game's frame.
        if (backend_.CaptureBackground()) ++captures_;
        else ++captureFailures_;
    }
    if (!visible_.load() || failed_.load()) return;
    if (renderer_.Render(backend_) == RenderResult::Failed) {
        Fail("draw_failed", backend_.LastError());
    }
}

bool DrawLayer::OnBeforeReset(IDirect3DDevice9* device) noexcept {
    if (stopped_.load() || device == nullptr) return false;
    try {
        if (!IsGameDevice(device)) return false;
        std::lock_guard<std::mutex> guard(deviceLock_);
        if (backend_.Device() == device) backend_.ReleaseDefaultPool();   // buffers and the background texture
        return true;
    } catch (...) {
        return false;
    }
}

void DrawLayer::OnAfterReset(bool handled, long result) noexcept {
    if (handled && result >= 0) ++deviceResets_;   // the default-pool objects are recreated lazily by the next frame
}

std::string DrawLayer::StatsJson() {
    const RenderStats s = renderer_.Stats();
    std::string out = "{\"ok\":true,\"scenes_drawn\":" + Num(s.framesDrawn);
    out += ",\"commands\":" + Num(s.commandsDrawn);
    out += ",\"unknown_cmds\":" + Num(s.unknownCommands);
    out += ",\"skipped_cmds\":" + Num(s.skippedCommands);
    out += ",\"images\":" + Num(static_cast<uint64_t>(renderer_.ImageCount()));
    out += ",\"fonts\":" + Num(static_cast<uint64_t>(fonts_.Count()));
    out += ",\"glyphs_drawn\":" + Num(s.glyphsDrawn);
    out += ",\"atlas_evictions\":" + Num(s.atlasEvictions);
    out += ",\"missing_images\":" + Num(s.missingImages);
    out += ",\"triangles\":" + Num(s.triangles);
    out += ",\"draw_calls\":" + Num(s.drawCalls);
    out += ",\"skipped_frames\":" + Num(backend_.SkippedFrames());
    out += ",\"device_resets\":" + Num(deviceResets_.load());
    out += ",\"view_w\":" + Num(static_cast<uint64_t>(backend_.ViewWidth()));
    out += ",\"view_h\":" + Num(static_cast<uint64_t>(backend_.ViewHeight()));
    out += ",\"captures\":" + Num(captures_.load());
    out += ",\"capture_failures\":" + Num(captureFailures_.load());
    out += std::string(",\"visible\":") + (visible_.load() ? "true" : "false");
    out += std::string(",\"failed\":") + (failed_.load() ? "true" : "false");
    out += std::string(",\"hooked\":") + (HooksInstalled() ? "true" : "false");
    out += ",\"last_error\":";
    json::AppendQuoted(out, LastError());
    out += "}";
    return out;
}

double DrawLayer::MeasureWidth(FontRole role, double size, const std::string& utf8) {
    const double scale = std::min<double>(kMeasureOversample, 400.0 / size);   // glyph sizes are capped at 400 px
    const int px = std::clamp(static_cast<int>(std::lround(size * scale)), 1, 400);
    double total = 0;
    for (const uint32_t cp : DecodeUtf8(utf8)) {
        GlyphBitmap glyph;
        total += glyphs_.Glyph(role, px, cp, glyph) ? glyph.advance : px * 0.5;
    }
    return total / scale;
}

int DrawLayer::Execute(const char* operation, const char* argumentsJson, char* buffer, uint32_t capacity, uint32_t* length) {
    if (operation == nullptr || length == nullptr) return 1;
    *length = 0;
    if (stopped_.load()) return 11;
    std::string answer;
    try {
        const std::string op(operation);
        const size_t cap = op == "image.put" ? kMaxImageOperationBytes : (op == "scene" ? kMaxSceneBytes : kMaxOperationBytes);
        std::string_view args = "{}";
        if (argumentsJson != nullptr) {
            size_t n = 0;
            while (n <= cap && argumentsJson[n] != 0) ++n;   // bounded scan: the caller's buffer may be unterminated garbage
            if (n > cap) {
                answer = ErrorJson("too_large", "the arguments exceed the limit of the operation");
                return Respond(answer, buffer, capacity, length);
            }
            args = std::string_view(argumentsJson, n);
        }

        if (op == "scene") {
            Scene scene;
            SceneError error;
            if (!ParseScene(args, scene, error)) {
                answer = ErrorJson(error.code.c_str(), error.detail);   // the previous scene stays
            } else {
                answer = "{\"ok\":true,\"commands\":" + Num(scene.cmds.size()) + ",\"unknown\":" + Num(static_cast<uint64_t>(scene.unknownCommands)) +
                         ",\"skipped\":" + Num(static_cast<uint64_t>(scene.skippedCommands)) + "}";
                renderer_.SetScene(std::make_shared<const Scene>(std::move(scene)));
            }
            return Respond(answer, buffer, capacity, length);
        }

        json::Value doc;
        json::Limits limits;
        limits.maxBytes = cap;
        limits.maxNodes = 64;
        const json::Result parsed = json::Parse(args, limits, doc);
        if (!parsed.ok || doc.type != json::Type::Object) {
            answer = ErrorJson("json_syntax", parsed.ok ? "the arguments must be a JSON object" : parsed.error);
            return Respond(answer, buffer, capacity, length);
        }

        if (op == "image.put") {
            std::string id, pixels;
            long long w = 0, h = 0;
            if (!StringMember(doc, "id", id) || id.empty() || id.size() > kMaxImageIdBytes || !IntMember(doc, "w", 1, 16384, w) ||
                !IntMember(doc, "h", 1, 16384, h) || !StringMember(doc, "rgba", pixels)) {
                answer = ErrorJson("bad_args", "image.put needs id, w, h and rgba");
            } else if (static_cast<unsigned long long>(w) * static_cast<unsigned long long>(h) * 4ull > kMaxImageBytes) {
                answer = ErrorJson("image_too_large", "an image is limited to 4 MiB of pixels");
            } else {
                const size_t expected = static_cast<size_t>(w) * static_cast<size_t>(h) * 4;
                std::vector<uint8_t> rgba;
                if (!Base64Decode(pixels, expected, rgba) || rgba.size() != expected) {
                    answer = ErrorJson("bad_pixels", "rgba must be base64 of exactly w*h*4 bytes");
                } else {
                    switch (renderer_.PutImage(id, static_cast<int>(w), static_cast<int>(h), std::move(rgba))) {
                    case ImageResult::Ok: answer = "{\"ok\":true,\"images\":" + Num(static_cast<uint64_t>(renderer_.ImageCount())) + "}"; break;
                    case ImageResult::TooMany: answer = ErrorJson("too_many_images", "at most 128 images"); break;
                    case ImageResult::TooBig: answer = ErrorJson("image_too_large", "an image is limited to 4 MiB of pixels"); break;
                    case ImageResult::OutOfMemoryBudget: answer = ErrorJson("image_budget", "the images would exceed the memory budget of the layer"); break;
                    default: answer = ErrorJson("bad_args", "invalid image"); break;
                    }
                }
            }
        } else if (op == "image.drop") {
            std::string id;
            if (!StringMember(doc, "id", id)) answer = ErrorJson("bad_args", "image.drop needs id");
            else answer = std::string("{\"ok\":true,\"existed\":") + (renderer_.DropImage(id) ? "true" : "false") + "}";
        } else if (op == "font.put") {
            std::string role, path;
            FontRole fontRole = FontRole::Regular;
            std::wstring widePath;
            if (!StringMember(doc, "role", role) || !RoleFromName(role, fontRole) || !StringMember(doc, "path", path)) {
                answer = ErrorJson("bad_args", "font.put needs a role (regular, medium, bold, black, condensed) and a path");
            } else if (!WideFromUtf8(path, widePath) || !IsAbsolutePath(widePath) || !HasFontExtension(widePath)) {
                answer = ErrorJson("bad_font_path", "the path must be absolute and name a .ttf or .otf file");
            } else {
                std::string error;
                if (fonts_.Put(fontRole, widePath, error)) answer = "{\"ok\":true,\"fonts\":" + Num(static_cast<uint64_t>(fonts_.Count())) + "}";
                else answer = ErrorJson("font_rejected", error);
            }
        } else if (op == "measure") {
            std::string text, role;
            FontRole fontRole = FontRole::Regular;
            const json::Value* size = doc.Find("size");
            if (!StringMember(doc, "text", text) || !StringMember(doc, "font", role) || !RoleFromName(role, fontRole) || size == nullptr ||
                size->type != json::Type::Number || !(size->number >= 1 && size->number <= 512)) {
                answer = ErrorJson("bad_args", "measure needs text, font (a role) and size (1..512)");
            } else if (text.size() > kMaxMeasureBytes) {
                answer = ErrorJson("too_large", "measure takes at most 2048 bytes of text");
            } else {
                answer = "{\"ok\":true,\"w\":" + FormatWidth(MeasureWidth(fontRole, size->number, text)) + "}";
            }
        } else if (op == "background.capture") {
            capturePending_.store(true);   // the next Present copies the frame the game just rendered
            releaseBackground_.store(false);
            answer = "{\"ok\":true,\"pending\":true}";
        } else if (op == "background.release") {
            capturePending_.store(false);
            releaseBackground_.store(true);
            answer = "{\"ok\":true}";
        } else if (op == "visible") {
            const json::Value* on = doc.Find("on");
            if (on == nullptr || on->type != json::Type::Bool) {
                answer = ErrorJson("bad_args", "visible needs on:true|false");
            } else {
                visible_.store(on->boolean);
                if (on->boolean) failed_.store(false);   // an explicit request to draw again retries after a failure
                answer = std::string("{\"ok\":true,\"visible\":") + (on->boolean ? "true" : "false") + "}";
            }
        } else if (op == "stats") {
            answer = StatsJson();
        } else {
            answer = ErrorJson("unknown_operation", "unknown operation");
        }
    } catch (const std::exception& e) {
        answer = ErrorJson("internal_error", e.what());
    } catch (...) {
        answer = ErrorJson("internal_error", "unexpected failure");
    }
    return Respond(answer, buffer, capacity, length);
}

} // namespace hud
