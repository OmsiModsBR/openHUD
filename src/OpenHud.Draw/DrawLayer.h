#pragma once
// The draw layer of the module: owns the renderer, the Direct3D 9 backend and the
// fonts, implements the operations of the scene protocol, and hooks the game
// device's Present and Reset. Operations may come from any thread; drawing happens
// on whatever thread the game presents on.
#include "D3D9Backend.h"
#include "Fonts.h"
#include "Renderer.h"
#include "omsilaunch_module_native_v1.h"

#include <atomic>
#include <memory>
#include <mutex>
#include <string>

struct IDirect3DDevice9;

namespace hud {

class DrawLayer {
public:
    DrawLayer();
    ~DrawLayer();
    DrawLayer(const DrawLayer&) = delete;
    DrawLayer& operator=(const DrawLayer&) = delete;

    // Module entry points (see omsilaunch_module_native_v1.h).
    int Start(const omsilaunch_bridge_v1* bridge);
    int GameplayReady(const omsilaunch_bridge_v1* bridge);
    void Stop();
    int Execute(const char* operation, const char* argumentsJson, char* buffer, uint32_t capacity, uint32_t* length);

    // Detour bodies; the static detours route here. Never throw.
    void OnPresent(IDirect3DDevice9* device) noexcept;
    bool OnBeforeReset(IDirect3DDevice9* device) noexcept;
    void OnAfterReset(bool handled, long result) noexcept;

    bool Stopped() const { return stopped_.load(); }
    bool Failed() const { return failed_.load(); }
    bool HooksInstalled() const { return presentHooked_ && resetHooked_; }
    std::string LastError() const;
    std::shared_ptr<const Scene> CurrentScene() const { return renderer_.CurrentScene(); }

private:
    const omsilaunch_bridge_v1* bridge_ = nullptr;
    FontRegistry fonts_;
    GdiGlyphSource glyphs_;
    Renderer renderer_;
    D3D9Backend backend_;

    std::mutex deviceLock_;        // guards the backend and everything Direct3D
    mutable std::mutex errorLock_;
    std::string lastError_;
    std::atomic<bool> stopped_{false};
    std::atomic<bool> visible_{true};
    std::atomic<bool> failed_{false};
    std::atomic<bool> capturePending_{false};
    std::atomic<bool> releaseBackground_{false};
    std::atomic<uint64_t> captures_{0};
    std::atomic<uint64_t> captureFailures_{0};
    std::atomic<uint64_t> deviceResets_{0};
    bool presentHooked_ = false;
    bool resetHooked_ = false;

    bool IsGameDevice(IDirect3DDevice9* device) const;
    int RequestHooks();
    void DrawFrame(IDirect3DDevice9* device);          // inside deviceLock_, C++ exceptions possible
    void Fail(const char* code, const std::string& detail) noexcept;
    void Emit(const char* name, const std::string& json) noexcept;
    std::string StatsJson();
    // Width in canvas pixels of one line of text with the registered fonts (glyph advances, no kerning),
    // the way DrawText lays it out. Safe from any thread.
    double MeasureWidth(FontRole role, double size, const std::string& utf8);
};

// The detours handed to the bridge. They call the original captured by request_hook
// (kept in statics: a thread may still be inside a detour after the layer stopped).
namespace hooks {
long __stdcall Present(IDirect3DDevice9* self, const void* source, const void* dest, void* window, const void* dirty);
long __stdcall Reset(IDirect3DDevice9* self, void* parameters);
// The layer the detours route to (null before start and after stop).
void SetActive(DrawLayer* layer);
void*& PresentOriginal();
void*& ResetOriginal();
} // namespace hooks

} // namespace hud
