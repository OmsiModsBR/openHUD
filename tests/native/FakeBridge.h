#pragma once
// A stand-in for the OmsiLaunch bridge table: records the hooks the module asks
// for, answers the game-device question, and collects emitted events.
#include "DrawLayer.h"

#include <d3d9.h>

#include <map>
#include <string>
#include <vector>

namespace kit {

struct FakeBridge {
    omsilaunch_bridge_v1 table = {};
    std::map<std::string, void*> detours;
    std::map<std::string, int> hookResult;   // forced results per target (default 0)
    std::vector<std::string> events;
    const void* gameDevice = nullptr;
    int deviceAnswerWhenNull = 10;
    // The "original" functions the hooks continue to.
    static inline int presentCalls = 0;
    static inline int resetCalls = 0;
    static inline IDirect3DDevice9* resetPerforms = nullptr;   // when set, the fake original really resets this device

    static HRESULT __stdcall OriginalPresent(IDirect3DDevice9*, const RECT*, const RECT*, HWND, const RGNDATA*) {
        ++presentCalls;
        return S_OK;
    }
    static HRESULT __stdcall OriginalReset(IDirect3DDevice9* device, D3DPRESENT_PARAMETERS* parameters) {
        ++resetCalls;
        (void)device;
        if (resetPerforms != nullptr) return resetPerforms->Reset(parameters);
        return S_OK;
    }

    static int __cdecl RequestHook(void* context, const char* target, void* detour, void** original) {
        auto* self = static_cast<FakeBridge*>(context);
        const auto forced = self->hookResult.find(target);
        if (forced != self->hookResult.end() && forced->second != 0) return forced->second;
        self->detours[target] = detour;
        const std::string name(target);
        if (name == "d3d9:device.Present") *original = reinterpret_cast<void*>(&OriginalPresent);
        else if (name == "d3d9:device.Reset") *original = reinterpret_cast<void*>(&OriginalReset);
        else return 2;
        return 0;
    }
    static int __cdecl Emit(void* context, const char* name, const char* data) {
        static_cast<FakeBridge*>(context)->events.push_back(std::string(name) + " " + (data ? data : "null"));
        return 0;
    }
    static int __cdecl IsGameDevice(void* context, const void* device) {
        auto* self = static_cast<FakeBridge*>(context);
        if (self->gameDevice == nullptr) return self->deviceAnswerWhenNull;
        return device == self->gameDevice ? 1 : 0;
    }

    FakeBridge() {
        table.size = sizeof(omsilaunch_bridge_v1);
        table.version = OMSILAUNCH_MODULE_NATIVE_V1;
        table.context = this;
        table.request_hook = &RequestHook;
        table.emit = &Emit;
        table.d3d_is_game_device = &IsGameDevice;
        presentCalls = resetCalls = 0;
        resetPerforms = nullptr;
    }

    long CallPresent(IDirect3DDevice9* device) {
        auto fn = reinterpret_cast<HRESULT(__stdcall*)(IDirect3DDevice9*, const RECT*, const RECT*, HWND, const RGNDATA*)>(detours.at("d3d9:device.Present"));
        return fn(device, nullptr, nullptr, nullptr, nullptr);
    }
    long CallReset(IDirect3DDevice9* device, D3DPRESENT_PARAMETERS* parameters) {
        auto fn = reinterpret_cast<HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRESENT_PARAMETERS*)>(detours.at("d3d9:device.Reset"));
        return fn(device, parameters);
    }
};

// Runs an operation and returns the JSON answer (empty on a non-zero return code).
inline std::string Run(hud::DrawLayer& layer, const char* operation, const std::string& arguments, int* code = nullptr) {
    std::vector<char> buffer(1 << 16);
    uint32_t length = 0;
    const int rc = layer.Execute(operation, arguments.c_str(), buffer.data(), static_cast<uint32_t>(buffer.size()), &length);
    if (code != nullptr) *code = rc;
    return rc == 0 ? std::string(buffer.data(), length) : std::string();
}

} // namespace kit
