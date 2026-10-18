#pragma once
// Shared helpers of the raster and Direct3D tests.
#include "Harness.h"

#include <windows.h>

#include "Fonts.h"
#include "Renderer.h"
#include "SoftBackend.h"

#include <algorithm>
#include <cmath>
#include <memory>
#include <string>

namespace kit {

struct Rgba {
    int r = 0, g = 0, b = 0, a = 0;
};

inline Rgba Px(const hud::SoftBackend& s, int x, int y) {
    uint8_t p[4];
    s.Pixel(x, y, p);
    return {p[0], p[1], p[2], p[3]};
}

inline bool Near(const Rgba& c, int r, int g, int b, int tol) {
    return std::abs(c.r - r) <= tol && std::abs(c.g - g) <= tol && std::abs(c.b - b) <= tol;
}

inline std::string Describe(const Rgba& c) {
    return "rgb(" + std::to_string(c.r) + "," + std::to_string(c.g) + "," + std::to_string(c.b) + ")";
}

// A font from the Windows fonts folder; used only by the tests (the module embeds none).
inline std::wstring WindowsFont(const wchar_t* file) {
    wchar_t dir[MAX_PATH];
    if (GetWindowsDirectoryW(dir, MAX_PATH) == 0) return L"";
    const std::wstring path = std::wstring(dir) + L"\\Fonts\\" + file;
    return GetFileAttributesW(path.c_str()) == INVALID_FILE_ATTRIBUTES ? L"" : path;
}

// Registers Arial (or Segoe UI) for the regular and bold roles. False when neither exists.
inline bool RegisterTestFonts(hud::FontRegistry& fonts) {
    std::string error;
    std::wstring regular = WindowsFont(L"arial.ttf"), bold = WindowsFont(L"arialbd.ttf");
    if (regular.empty() || bold.empty()) {
        regular = WindowsFont(L"segoeui.ttf");
        bold = WindowsFont(L"segoeuib.ttf");
    }
    if (regular.empty() || bold.empty()) return false;
    return fonts.Put(hud::FontRole::Regular, regular, error) && fonts.Put(hud::FontRole::Bold, bold, error);
}

// A renderer wired to a software backend.
struct Fixture {
    hud::FontRegistry fonts;
    hud::GdiGlyphSource glyphs;
    hud::Renderer renderer;
    hud::SoftBackend soft;

    Fixture(int w, int h) : glyphs(fonts), renderer(glyphs), soft(w, h) {}

    void Load(const std::string& json) {
        hud::Scene scene;
        hud::SceneError error;
        if (!hud::ParseScene(json, scene, error)) throw t::Failure{"scene refused: " + error.code + " " + error.detail};
        renderer.SetScene(std::make_shared<const hud::Scene>(std::move(scene)));
    }
    hud::RenderResult Draw() { return renderer.Render(soft); }
    // Loads and draws on a cleared surface.
    void Run(const std::string& json, uint32_t clear = 0xFF000000u) {
        soft.Clear(clear);
        Load(json);
        if (Draw() == hud::RenderResult::Failed) throw t::Failure{"render failed"};
    }
};

inline std::string Doc(const std::string& commands, int w = 100, int h = 100, bool background = false) {
    return "{\"v\":1,\"w\":" + std::to_string(w) + ",\"h\":" + std::to_string(h) + ",\"background\":" + (background ? "true" : "false") +
           ",\"cmds\":[" + commands + "]}";
}

// Bounding box of pixels that differ from `background` by more than `threshold` in any channel.
struct Box {
    int x0 = 1 << 30, y0 = 1 << 30, x1 = -1, y1 = -1, count = 0;
    bool Empty() const { return count == 0; }
};

inline Box Changed(const hud::SoftBackend& s, int rx0, int ry0, int rx1, int ry1, const Rgba& background, int threshold = 24) {
    Box b;
    for (int y = ry0; y < ry1; ++y) {
        for (int x = rx0; x < rx1; ++x) {
            const Rgba c = Px(s, x, y);
            if (std::abs(c.r - background.r) > threshold || std::abs(c.g - background.g) > threshold || std::abs(c.b - background.b) > threshold) {
                b.x0 = std::min(b.x0, x); b.y0 = std::min(b.y0, y);
                b.x1 = std::max(b.x1, x); b.y1 = std::max(b.y1, y);
                ++b.count;
            }
        }
    }
    return b;
}

// Sum of red-channel coverage (0..1) over a region of a white-on-black render.
inline double Coverage(const hud::SoftBackend& s, int x0, int y0, int x1, int y1) {
    double sum = 0;
    for (int y = y0; y < y1; ++y) {
        for (int x = x0; x < x1; ++x) sum += Px(s, x, y).r / 255.0;
    }
    return sum;
}

} // namespace kit
