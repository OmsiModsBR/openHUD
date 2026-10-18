#include "DrawLayer.h"

// openHUD's draw layer (omsilaunch_module_native_v1). The exports are thin: the
// layer, its hooks and the scene operations live in DrawLayer.
namespace {
// Never destroyed: the DLL stays loaded and inert until OMSI exits, and a thread
// may still be returning through a detour after stop.
hud::DrawLayer* gLayer = nullptr;
} // namespace

extern "C" __declspec(dllexport) int __cdecl omsilaunch_module_native_v1_start(const omsilaunch_bridge_v1* bridge) {
    if (bridge == nullptr || bridge->version != OMSILAUNCH_MODULE_NATIVE_V1) return 1;
    try {
        if (gLayer == nullptr) gLayer = new hud::DrawLayer();
        return gLayer->Start(bridge);
    } catch (...) {
        return 8;
    }
}

extern "C" __declspec(dllexport) int __cdecl omsilaunch_module_native_v1_gameplay_ready(const omsilaunch_bridge_v1* bridge) {
    if (gLayer == nullptr) return 1;
    try {
        return gLayer->GameplayReady(bridge);
    } catch (...) {
        return 8;
    }
}

extern "C" __declspec(dllexport) void __cdecl omsilaunch_module_native_v1_stop(void) {
    if (gLayer == nullptr) return;
    try {
        gLayer->Stop();
    } catch (...) {
    }
}

extern "C" __declspec(dllexport) int __cdecl omsilaunch_module_native_v1_execute(const omsilaunch_bridge_v1* bridge, const char* operation, const char* argumentsJson, char* buffer, uint32_t capacity, uint32_t* length) {
    (void)bridge;
    if (gLayer == nullptr || length == nullptr) return 1;
    try {
        return gLayer->Execute(operation, argumentsJson, buffer, capacity, length);
    } catch (...) {
        return 1;
    }
}
