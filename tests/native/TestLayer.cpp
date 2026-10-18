#include "Kit.h"

#include "Atlas.h"
#include "FakeBridge.h"

#include <atomic>
#include <cstdlib>
#include <list>
#include <map>
#include <thread>

using namespace hud;
using namespace kit;

namespace {

bool Has(const std::string& text, const char* needle) { return text.find(needle) != std::string::npos; }

std::string Base64Encode(const std::vector<uint8_t>& data) {
    static const char* alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string out;
    for (size_t i = 0; i < data.size(); i += 3) {
        const uint32_t n = (static_cast<uint32_t>(data[i]) << 16) | (i + 1 < data.size() ? static_cast<uint32_t>(data[i + 1]) << 8 : 0) |
                           (i + 2 < data.size() ? data[i + 2] : 0u);
        out.push_back(alphabet[(n >> 18) & 63]);
        out.push_back(alphabet[(n >> 12) & 63]);
        out.push_back(i + 1 < data.size() ? alphabet[(n >> 6) & 63] : '=');
        out.push_back(i + 2 < data.size() ? alphabet[n & 63] : '=');
    }
    return out;
}

std::string ImagePut(const std::string& id, int w, int h, const std::vector<uint8_t>& pixels) {
    return "{\"id\":\"" + id + "\",\"w\":" + std::to_string(w) + ",\"h\":" + std::to_string(h) + ",\"rgba\":\"" + Base64Encode(pixels) + "\"}";
}

// A started layer with no Direct3D device behind it.
struct Started {
    FakeBridge bridge;
    DrawLayer layer;
    Started() {
        bridge.hookResult["d3d9:device.Present"] = 10;
        bridge.hookResult["d3d9:device.Reset"] = 10;
        if (layer.Start(&bridge.table) != 0) throw t::Failure{"Start failed"};
    }
    ~Started() { layer.Stop(); }
};

} // namespace

TEST(atlas_basic, "atlas.lru-order") {
    LruAtlas a(32, 2, 2);
    CHECK(a.Capacity() == 4 && a.TextureWidth() == 64 && a.TextureHeight() == 64);
    bool evicted = true;
    uint64_t gone = 0;
    for (uint64_t k = 1; k <= 4; ++k) {
        CHECK(a.Acquire(k, evicted, gone) == static_cast<int>(k - 1) && !evicted);
    }
    CHECK(a.Count() == 4 && a.Evictions() == 0);
    CHECK(a.CellX(3) == 32 && a.CellY(3) == 32 && a.CellX(1) == 32 && a.CellY(1) == 0 && a.CellY(2) == 32);
    // Touching key 1 makes key 2 the least recently used.
    CHECK(a.Find(1) == 0);
    CHECK(a.Acquire(5, evicted, gone) == 1 && evicted && gone == 2);
    CHECK(a.Find(2) == -1 && a.Find(5) == 1);
    CHECK(a.Acquire(6, evicted, gone) == 2 && evicted && gone == 3);
    CHECK(a.Acquire(7, evicted, gone) == 3 && evicted && gone == 4);
    CHECK(a.Acquire(8, evicted, gone) == 0 && evicted && gone == 1);   // key 1 was touched earlier than 5, 6, 7
    CHECK(a.Evictions() == 4 && a.Count() == 4);
    CHECK(a.Find(999) == -1);
    a.Clear();
    CHECK(a.Count() == 0 && a.Find(5) == -1 && a.Acquire(1, evicted, gone) == 0 && !evicted);
}

TEST(atlas_model, "atlas.lru-against-a-model") {
    // 200000 random operations against a list-based reference: the same cells, the same victims.
    const int capacity = 16;
    LruAtlas a(8, 4, 4);
    std::list<std::pair<uint64_t, int>> model;   // front = most recently used
    uint32_t seed = 7;
    const auto next = [&]() { seed = seed * 1664525u + 1013904223u; return seed >> 8; };
    for (int i = 0; i < 200000; ++i) {
        const uint64_t key = next() % 40;
        auto it = model.begin();
        while (it != model.end() && it->first != key) ++it;
        const int found = a.Find(key);
        if (it != model.end()) {
            CHECK(found == it->second);
            model.splice(model.begin(), model, it);
        } else {
            CHECK(found == -1);
            bool evicted = false;
            uint64_t gone = 0;
            const int cell = a.Acquire(key, evicted, gone);
            if (static_cast<int>(model.size()) < capacity) {
                CHECK(!evicted);   // a free cell exists
            } else {
                CHECK(evicted && gone == model.back().first);
                CHECK(cell == model.back().second);   // the victim's cell is reused
                model.pop_back();
            }
            CHECK(cell >= 0 && cell < capacity);
            for (const auto& entry : model) CHECK(entry.second != cell);   // never hands out a cell in use
            model.emplace_front(key, cell);
        }
        CHECK(a.Count() == static_cast<int>(model.size()));
    }
}

TEST(layer_start, "layer.start-requirements") {
    FakeBridge fake;
    DrawLayer layer;
    CHECK(layer.Start(nullptr) == 1);
    fake.table.version = 9;
    CHECK(layer.Start(&fake.table) == 1);
    fake.table.version = OMSILAUNCH_MODULE_NATIVE_V1;
    fake.table.size = OMSILAUNCH_BRIDGE_V1_SIZE_WITH_RUN_OPERATION;   // a protocol 2 bridge cannot name the game device
    CHECK(layer.Start(&fake.table) == 1);
    fake.table.size = sizeof(omsilaunch_bridge_v1);
    fake.table.d3d_is_game_device = nullptr;
    CHECK(layer.Start(&fake.table) == 1);
    CHECK(fake.detours.empty());
    layer.Stop();
}

TEST(layer_hooks, "layer.hook-requests") {
    {   // The device does not exist yet at start (10): the hooks come in gameplay_ready.
        FakeBridge fake;
        DrawLayer layer;
        fake.hookResult["d3d9:device.Present"] = 10;
        fake.hookResult["d3d9:device.Reset"] = 10;
        CHECK(layer.Start(&fake.table) == 0 && fake.detours.empty() && !layer.HooksInstalled());
        CHECK(layer.GameplayReady(&fake.table) == 10);   // still no device: the phase fails
        fake.hookResult.clear();
        CHECK(layer.GameplayReady(&fake.table) == 0);
        CHECK(fake.detours.count("d3d9:device.Present") == 1 && fake.detours.count("d3d9:device.Reset") == 1 && layer.HooksInstalled());
        bool announced = false;
        for (const std::string& e : fake.events) announced = announced || e.rfind("hooks ", 0) == 0;
        CHECK(announced);
        layer.Stop();
    }
    {   // Both available at start: nothing left for gameplay_ready to request.
        FakeBridge fake;
        DrawLayer layer;
        CHECK(layer.Start(&fake.table) == 0 && layer.HooksInstalled());
        fake.detours.clear();
        CHECK(layer.GameplayReady(&fake.table) == 0 && fake.detours.empty());
        layer.Stop();
    }
    {   // A refusal other than "no device yet" fails start (the bridge removes what was installed).
        FakeBridge fake;
        DrawLayer layer;
        fake.hookResult["d3d9:device.Present"] = 4;
        CHECK(layer.Start(&fake.table) == 4);
        layer.Stop();
    }
    {
        FakeBridge fake;
        DrawLayer layer;
        fake.hookResult["d3d9:device.Reset"] = 11;
        CHECK(layer.Start(&fake.table) == 11);
        layer.Stop();
    }
}

TEST(layer_scene_op, "layer.op-scene") {
    Started s;
    int rc = -1;
    std::string a = Run(s.layer, "scene", "{\"v\":1,\"w\":100,\"h\":100,\"cmds\":[{\"t\":\"rect\",\"x\":1,\"y\":1,\"w\":5,\"h\":5},{\"t\":\"warp\"},{\"t\":\"rect\"}]}", &rc);
    CHECK(rc == 0 && Has(a, "\"ok\":true") && Has(a, "\"commands\":1") && Has(a, "\"unknown\":1") && Has(a, "\"skipped\":1"));
    const auto kept = s.layer.CurrentScene();
    CHECK(kept && kept->cmds.size() == 1);
    // Anything malformed answers ok:false and keeps the scene that was there.
    const char* bad[] = {"not json", "[]", "{\"v\":3,\"cmds\":[]}", "{\"v\":1,\"cmds\":[NaN]}", "{\"v\":1,\"cmds\":5}", ""};
    for (const char* b : bad) {
        a = Run(s.layer, "scene", b, &rc);
        CHECK_MSG(rc == 0 && Has(a, "\"ok\":false") && Has(a, "\"code\":"), b);
        CHECK(s.layer.CurrentScene() == kept);
    }
    std::string huge = "{\"v\":1,\"cmds\":[" + std::string((1u << 20) + 10, ' ') + "]}";
    a = Run(s.layer, "scene", huge, &rc);
    CHECK(Has(a, "\"ok\":false") && s.layer.CurrentScene() == kept);
    // An empty scene replaces it: nothing is drawn.
    a = Run(s.layer, "scene", "{}", &rc);
    CHECK(Has(a, "\"ok\":true") && s.layer.CurrentScene() != kept && s.layer.CurrentScene()->cmds.empty());
    // Unknown operations and bad arguments.
    a = Run(s.layer, "frobnicate", "{}", &rc);
    CHECK(rc == 0 && Has(a, "\"ok\":false") && Has(a, "unknown_operation"));
    a = Run(s.layer, "visible", "[]", &rc);
    CHECK(Has(a, "\"ok\":false"));
    a = Run(s.layer, "visible", "{\"on\":\"yes\"}", &rc);
    CHECK(Has(a, "\"ok\":false") && Has(a, "bad_args"));
    CHECK(Has(Run(s.layer, "stats", "", &rc), "\"ok\":false"));   // an empty argument document is not JSON
    std::vector<char> buffer(4096);
    uint32_t length = 0;
    CHECK(s.layer.Execute("stats", nullptr, buffer.data(), static_cast<uint32_t>(buffer.size()), &length) == 0);   // no arguments means {}
    CHECK(std::string(buffer.data(), length).find("\"ok\":true") != std::string::npos);
}

TEST(layer_buffer, "layer.output-buffer-convention") {
    Started s;
    char tiny[8];
    uint32_t length = 0;
    CHECK(s.layer.Execute("stats", "{}", tiny, sizeof(tiny), &length) == OMSILAUNCH_BRIDGE_BUFFER_TOO_SMALL && length > sizeof(tiny));
    std::vector<char> exact(length + 1);
    uint32_t again = 0;
    CHECK(s.layer.Execute("stats", "{}", exact.data(), static_cast<uint32_t>(exact.size()), &again) == 0 && again == length && exact[length] == 0);
    std::vector<char> one_short(length);
    CHECK(s.layer.Execute("stats", "{}", one_short.data(), static_cast<uint32_t>(one_short.size()), &again) == OMSILAUNCH_BRIDGE_BUFFER_TOO_SMALL && again == length);
    CHECK(s.layer.Execute("stats", "{}", nullptr, 0, &again) == OMSILAUNCH_BRIDGE_BUFFER_TOO_SMALL && again == length);
    CHECK(s.layer.Execute(nullptr, "{}", exact.data(), 100, &again) == 1);
    CHECK(s.layer.Execute("stats", "{}", exact.data(), 100, nullptr) == 1);
}

TEST(layer_image_op, "layer.op-images") {
    Started s;
    int rc = 0;
    const std::vector<uint8_t> px(16, 200);
    std::string a = Run(s.layer, "image.put", ImagePut("a", 2, 2, px), &rc);
    CHECK(rc == 0 && Has(a, "\"ok\":true") && Has(a, "\"images\":1"));
    CHECK(Has(Run(s.layer, "image.put", ImagePut("a", 2, 2, px)), "\"images\":1"));   // replacing keeps one
    CHECK(Has(Run(s.layer, "image.put", ImagePut("a", 2, 3, px)), "bad_pixels"));      // 16 bytes for a 2x3 image
    CHECK(Has(Run(s.layer, "image.put", "{\"id\":\"b\",\"w\":2,\"h\":2,\"rgba\":\"!!!!\"}"), "bad_pixels"));
    CHECK(Has(Run(s.layer, "image.put", "{\"id\":\"b\",\"w\":2,\"h\":2}"), "bad_args"));
    CHECK(Has(Run(s.layer, "image.put", "{\"id\":\"\",\"w\":2,\"h\":2,\"rgba\":\"AAAA\"}"), "bad_args"));
    CHECK(Has(Run(s.layer, "image.put", "{\"id\":\"b\",\"w\":2.5,\"h\":2,\"rgba\":\"AAAA\"}"), "bad_args"));
    CHECK(Has(Run(s.layer, "image.put", "{\"id\":\"b\",\"w\":0,\"h\":2,\"rgba\":\"\"}"), "bad_args"));
    CHECK(Has(Run(s.layer, "image.put", "{\"id\":\"b\",\"w\":16385,\"h\":1,\"rgba\":\"AAAA\"}"), "bad_args"));
    CHECK(Has(Run(s.layer, "image.put", "{\"id\":\"b\",\"w\":2048,\"h\":2048,\"rgba\":\"AAAA\"}"), "image_too_large"));   // 16 MiB of pixels
    // 4 MiB exactly is accepted; one pixel more is not.
    CHECK(Has(Run(s.layer, "image.put", ImagePut("max", 1024, 1024, std::vector<uint8_t>(4u << 20, 7))), "\"ok\":true"));
    CHECK(Has(Run(s.layer, "image.put", "{\"id\":\"b\",\"w\":1025,\"h\":1024,\"rgba\":\"AAAA\"}"), "image_too_large"));
    CHECK(Has(Run(s.layer, "image.drop", "{\"id\":\"max\"}"), "\"existed\":true"));
    CHECK(Has(Run(s.layer, "image.drop", "{\"id\":\"max\"}"), "\"existed\":false"));
    CHECK(Has(Run(s.layer, "image.drop", "{}"), "bad_args"));
    // 128 images at most.
    for (int i = 0; i < 127; ++i) CHECK(Has(Run(s.layer, "image.put", ImagePut("i" + std::to_string(i), 1, 1, {1, 2, 3, 4})), "\"ok\":true"));
    CHECK(Has(Run(s.layer, "image.put", ImagePut("one-too-many", 1, 1, {1, 2, 3, 4})), "too_many_images"));
    CHECK(Has(Run(s.layer, "stats", "{}"), "\"images\":128"));
}

TEST(layer_font_op, "layer.op-fonts") {
    Started s;
    const std::wstring arial = WindowsFont(L"arial.ttf");
    const auto narrow = [](const std::wstring& w) {
        std::string out;
        for (const wchar_t c : w) {
            if (c == L'\\') out += "\\\\";
            else out.push_back(static_cast<char>(c));
        }
        return out;
    };
    CHECK(Has(Run(s.layer, "font.put", "{\"role\":\"bold\"}"), "bad_args"));
    CHECK(Has(Run(s.layer, "font.put", "{\"role\":\"fancy\",\"path\":\"C:\\\\x.ttf\"}"), "bad_args"));
    CHECK(Has(Run(s.layer, "font.put", "{\"role\":\"bold\",\"path\":\"fonts\\\\bold.ttf\"}"), "bad_font_path"));       // relative
    CHECK(Has(Run(s.layer, "font.put", "{\"role\":\"bold\",\"path\":\"C:\\\\Windows\\\\notepad.exe\"}"), "bad_font_path"));   // not a font extension
    CHECK(Has(Run(s.layer, "font.put", "{\"role\":\"bold\",\"path\":\"C:\\\\no\\\\such\\\\file.ttf\"}"), "font_rejected"));
    CHECK(Has(Run(s.layer, "font.put", "{\"role\":\"bold\",\"path\":\"\"}"), "bad_font_path"));
    if (arial.empty()) SKIP("no Arial in the Windows fonts folder");
    const std::string ok = Run(s.layer, "font.put", "{\"role\":\"regular\",\"path\":\"" + narrow(arial) + "\"}");
    CHECK_MSG(Has(ok, "\"ok\":true") && Has(ok, "\"fonts\":1"), ok);
    CHECK(Has(Run(s.layer, "stats", "{}"), "\"fonts\":1"));
    for (const char* role : {"medium", "bold", "black", "condensed"}) CHECK(Has(Run(s.layer, "font.put", std::string("{\"role\":\"") + role + "\",\"path\":\"" + narrow(arial) + "\"}"), "\"ok\":true"));
    CHECK(Has(Run(s.layer, "stats", "{}"), "\"fonts\":5"));
}

TEST(layer_misc_ops, "layer.op-visible-background-stats") {
    Started s;
    std::string a = Run(s.layer, "visible", "{\"on\":false}");
    CHECK(Has(a, "\"visible\":false") && Has(Run(s.layer, "stats", "{}"), "\"visible\":false"));
    CHECK(Has(Run(s.layer, "visible", "{\"on\":true}"), "\"visible\":true"));
    CHECK(Has(Run(s.layer, "background.capture", "{}"), "\"pending\":true"));
    CHECK(Has(Run(s.layer, "background.release", "{}"), "\"ok\":true"));
    a = Run(s.layer, "stats", "{}");
    for (const char* key : {"scenes_drawn", "commands", "images", "fonts", "skipped_frames", "device_resets", "last_error", "unknown_cmds", "captures", "hooked"}) CHECK_MSG(Has(a, key), key);
}

namespace {
// The "w" member of a measure answer, or -1.
double Width(const std::string& answer) {
    const size_t at = answer.find("\"w\":");
    return at == std::string::npos || !Has(answer, "\"ok\":true") ? -1.0 : std::atof(answer.c_str() + at + 4);
}
std::string Measure(DrawLayer& layer, const std::string& text, double size, const char* font = "regular") {
    return Run(layer, "measure", "{\"text\":\"" + text + "\",\"size\":" + std::to_string(size) + ",\"font\":\"" + font + "\"}");
}
} // namespace

TEST(layer_measure, "layer.op-measure") {
    Started s;
    const double empty = Width(Measure(s.layer, "", 20));
    CHECK(empty == 0.0);
    const double narrow = Width(Measure(s.layer, "iiii", 20)), wide = Width(Measure(s.layer, "MMMM", 20));
    CHECK_MSG(narrow > 0 && wide > narrow * 1.5, "iiii=" + std::to_string(narrow) + " MMMM=" + std::to_string(wide));
    // Widths grow with the text and with the size, and keep a fraction (not whole pixels).
    const double one = Width(Measure(s.layer, "abc", 20)), two = Width(Measure(s.layer, "abcabc", 20));
    CHECK(std::abs(two - 2 * one) < 0.02 * two + 0.01);
    const double big = Width(Measure(s.layer, "Hello world", 40)), little = Width(Measure(s.layer, "Hello world", 20));
    CHECK_MSG(big > little * 1.9 && big < little * 2.1, std::to_string(big) + " vs " + std::to_string(little));
    CHECK(Width(Measure(s.layer, "Hello", 13.5)) > 0);
    // Every role answers; a character beyond the Basic Multilingual Plane counts as half an em.
    for (const char* role : {"regular", "medium", "bold", "black", "condensed"}) CHECK_MSG(Width(Measure(s.layer, "Role", 18, role)) > 0, role);
    CHECK(std::abs(Width(Run(s.layer, "measure", "{\"text\":\"" "\xF0\x9F\x98\x80" "\",\"size\":20,\"font\":\"bold\"}")) - 10.0) < 0.5);
    // A glyph size beyond the layer's cap is measured at a littleer scale, not refused.
    CHECK(Width(Measure(s.layer, "W", 512)) > 100);
    // Bad arguments answer ok:false and change nothing.
    const char* bad[] = {"{}", "{\"text\":\"a\",\"size\":20}", "{\"text\":\"a\",\"size\":20,\"font\":\"fancy\"}", "{\"text\":\"a\",\"size\":0,\"font\":\"bold\"}",
                         "{\"text\":\"a\",\"size\":513,\"font\":\"bold\"}", "{\"text\":5,\"size\":20,\"font\":\"bold\"}", "{\"text\":\"a\",\"size\":\"20\",\"font\":\"bold\"}", "[]"};
    for (const char* b : bad) CHECK_MSG(Has(Run(s.layer, "measure", b), "\"ok\":false"), b);
    CHECK(Has(Run(s.layer, "measure", "{\"text\":\"" + std::string(3000, 'a') + "\",\"size\":20,\"font\":\"bold\"}"), "too_large"));
    // The scene and the images are untouched by measuring.
    CHECK(Has(Run(s.layer, "stats", "{}"), "\"images\":0"));
}

TEST(layer_measure_threads, "layer.op-measure-from-several-threads") {
    Started s;
    const double expected = Width(Measure(s.layer, "Concurrent text", 16));
    CHECK(expected > 0);
    std::atomic<int> wrong{0};
    std::vector<std::thread> workers;
    for (int t = 0; t < 4; ++t) {
        workers.emplace_back([&]() {
            for (int i = 0; i < 300; ++i) {
                if (std::abs(Width(Measure(s.layer, "Concurrent text", 16)) - expected) > 1e-9) ++wrong;
                if (Width(Measure(s.layer, "Another", 12 + (i % 5))) <= 0) ++wrong;
            }
        });
    }
    for (auto& w : workers) w.join();
    CHECK_MSG(wrong.load() == 0, std::to_string(wrong.load()) + " wrong answers");
}

TEST(layer_stats_view, "layer.stats-reports-the-view-size") {
    Started s;
    const std::string a = Run(s.layer, "stats", "{}");
    CHECK(Has(a, "\"view_w\":0") && Has(a, "\"view_h\":0"));   // no frame has begun
}

TEST(layer_stop, "layer.stop-makes-everything-inert") {
    FakeBridge fake;
    DrawLayer layer;
    CHECK(layer.Start(&fake.table) == 0);
    CHECK(Has(Run(layer, "scene", "{\"v\":1,\"cmds\":[{\"t\":\"pop\"}]}"), "\"ok\":true"));
    fake.gameDevice = reinterpret_cast<const void*>(static_cast<uintptr_t>(0x40));
    layer.Stop();
    int rc = 0;
    Run(layer, "stats", "{}", &rc);
    CHECK(rc == 11);
    CHECK(layer.Stopped() && layer.CurrentScene() == nullptr);
    // A thread that was inside a detour continues down the chain; the stopped layer does not touch the device.
    CHECK(fake.CallPresent(reinterpret_cast<IDirect3DDevice9*>(static_cast<uintptr_t>(0x40))) == S_OK && FakeBridge::presentCalls == 1);
    CHECK(fake.CallReset(reinterpret_cast<IDirect3DDevice9*>(static_cast<uintptr_t>(0x40)), nullptr) == S_OK && FakeBridge::resetCalls == 1);
    layer.Stop();   // idempotent
}

TEST(layer_hostile_device, "layer.failure-disables-drawing-and-never-throws") {
    FakeBridge fake;
    DrawLayer layer;
    CHECK(layer.Start(&fake.table) == 0);
    CHECK(Has(Run(layer, "scene", "{\"v\":1,\"cmds\":[{\"t\":\"rect\",\"x\":0,\"y\":0,\"w\":5,\"h\":5,\"fill\":\"#ffffff\"}]}"), "\"ok\":true"));
    // The bridge says this pointer is the game's device, but it points at nothing: the draw faults.
    IDirect3DDevice9* bogus = reinterpret_cast<IDirect3DDevice9*>(static_cast<uintptr_t>(0x10));
    fake.gameDevice = bogus;
    CHECK(fake.CallPresent(bogus) == S_OK);   // the original still runs
    CHECK(FakeBridge::presentCalls == 1 && layer.Failed());
    CHECK_MSG(Has(layer.LastError(), "access_violation"), layer.LastError());
    bool reported = false;
    for (const std::string& e : fake.events) reported = reported || e.rfind("draw_failed ", 0) == 0;
    CHECK(reported);
    const size_t events = fake.events.size();
    CHECK(fake.CallPresent(bogus) == S_OK && FakeBridge::presentCalls == 2);
    CHECK(fake.events.size() == events);   // disabled: no second attempt, no second report
    CHECK(Has(Run(layer, "stats", "{}"), "\"failed\":true"));
    CHECK(Has(Run(layer, "visible", "{\"on\":true}"), "\"ok\":true") && !layer.Failed());
    layer.Stop();
}

TEST(layer_other_device, "layer.draws-only-on-the-game-device") {
    FakeBridge fake;
    DrawLayer layer;
    CHECK(layer.Start(&fake.table) == 0);
    CHECK(Has(Run(layer, "scene", "{\"v\":1,\"cmds\":[{\"t\":\"rect\",\"x\":0,\"y\":0,\"w\":5,\"h\":5,\"fill\":\"#ffffff\"}]}"), "\"ok\":true"));
    IDirect3DDevice9* other = reinterpret_cast<IDirect3DDevice9*>(static_cast<uintptr_t>(0x10));
    fake.gameDevice = reinterpret_cast<const void*>(static_cast<uintptr_t>(0x20));   // someone else is the game's device
    CHECK(fake.CallPresent(other) == S_OK && FakeBridge::presentCalls == 1 && !layer.Failed());   // untouched: no fault, no failure
    fake.gameDevice = nullptr;
    fake.deviceAnswerWhenNull = 10;   // the bridge observes no device yet
    CHECK(fake.CallPresent(other) == S_OK && !layer.Failed());
    fake.deviceAnswerWhenNull = 11;
    CHECK(fake.CallPresent(other) == S_OK && !layer.Failed());
    CHECK(fake.CallReset(other, nullptr) == S_OK && FakeBridge::resetCalls == 1);
    layer.Stop();
}
