#include <windows.h>
#include "omsilaunch_module_native_v1.h"

// openHUD's draw layer (omsilaunch_module_native_v1). The skeleton only starts
// and stops cleanly; the Present/Reset hooks and the drawing arrive with the
// draw-layer phase.
namespace {
const omsilaunch_bridge_v1* gBridge = nullptr;
}

extern "C" __declspec(dllexport) int __cdecl omsilaunch_module_native_v1_start(const omsilaunch_bridge_v1* bridge) {
    if (bridge == nullptr || bridge->version != OMSILAUNCH_MODULE_NATIVE_V1) return 1;
    gBridge = bridge;
    return 0;
}

extern "C" __declspec(dllexport) void __cdecl omsilaunch_module_native_v1_stop(void) {
    gBridge = nullptr;
}
