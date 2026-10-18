#pragma once
// 2D geometry for the renderer: affine transforms, path builders and the
// anti-aliased fill/stroke tessellators. Everything here works in device pixels.
// Anti-aliasing is geometric: a solid core plus a one pixel fringe whose vertex
// alpha ramps from the colour's alpha to zero, centred on the true edge.
#include "Backend.h"
#include "Scene.h"

#include <cstdint>
#include <vector>

namespace hud {

struct Pt {
    float x = 0, y = 0;
};

// x' = a*x + c*y + e ; y' = b*x + d*y + f
struct Mat {
    float a = 1, b = 0, c = 0, d = 1, e = 0, f = 0;

    Pt Apply(Pt p) const { return {a * p.x + c * p.y + e, b * p.x + d * p.y + f}; }
    // this * inner: applying the result is applying inner first, then this.
    Mat Times(const Mat& inner) const;
    float Scale() const;            // sqrt(|det|): the average linear scale
    bool AxisAligned() const;       // no rotation or skew (b == c == 0)
    static Mat Translate(float x, float y);
    static Mat ScaleBy(float sx, float sy);
    static Mat Rotate(float degrees);   // clockwise on screen (y down)
};

struct Mesh {
    std::vector<Vtx> v;
    std::vector<uint32_t> idx;
    void Clear() { v.clear(); idx.clear(); }
    uint32_t Add(float x, float y, uint32_t color, float u = 0, float vv = 0) {
        v.push_back({x, y, u, vv, color});
        return static_cast<uint32_t>(v.size() - 1);
    }
    void Tri(uint32_t a, uint32_t b, uint32_t c) {
        idx.push_back(a); idx.push_back(b); idx.push_back(c);
    }
    void Quad(uint32_t a, uint32_t b, uint32_t c, uint32_t d) { Tri(a, b, c); Tri(a, c, d); }
};

uint32_t ScaleAlpha(uint32_t argb, float factor);

// Path builders (local coordinates are transformed by m; `scale` picks the tessellation density).
void BuildRoundRect(std::vector<Pt>& out, float x, float y, float w, float h, float r, const Mat& m);
void BuildCircle(std::vector<Pt>& out, float cx, float cy, float r, const Mat& m);
// Angles in degrees, 0 = up, clockwise. Returns true when the sweep is a full turn.
bool BuildArc(std::vector<Pt>& out, float cx, float cy, float r, float a0, float a1, const Mat& m);

// Filled, anti-aliased polygon (any simple polygon; concave is triangulated by ear clipping).
void AddFill(Mesh& mesh, std::vector<Pt> ring, uint32_t argb);
// Stroked, anti-aliased polyline centred on the path.
void AddStroke(Mesh& mesh, std::vector<Pt> pts, bool closed, float width, uint32_t argb, Cap cap);

} // namespace hud
