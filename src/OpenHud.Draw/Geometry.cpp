#include "Geometry.h"

#include <algorithm>
#include <cmath>

namespace hud {

namespace {
constexpr float kPi = 3.14159265358979f;
constexpr float kDegToRad = kPi / 180.0f;

float Len(Pt p) { return std::sqrt(p.x * p.x + p.y * p.y); }
Pt Sub(Pt a, Pt b) { return {a.x - b.x, a.y - b.y}; }
Pt Add(Pt a, Pt b) { return {a.x + b.x, a.y + b.y}; }
Pt Mul(Pt a, float s) { return {a.x * s, a.y * s}; }
float Dot(Pt a, Pt b) { return a.x * b.x + a.y * b.y; }
float Cross(Pt a, Pt b) { return a.x * b.y - a.y * b.x; }

Pt Norm(Pt p) {
    const float l = Len(p);
    return l > 1e-12f ? Pt{p.x / l, p.y / l} : Pt{0, 0};
}

uint32_t Transparent(uint32_t argb) { return argb & 0x00FFFFFFu; }

// Number of segments for an arc of `sweep` radians at device radius r (0.1 px sagitta).
int Segments(float radius, float sweep) {
    if (radius < 0.5f) return 2;
    const float step = 2.0f * std::acos(std::max(0.0f, 1.0f - 0.1f / radius));
    const int n = static_cast<int>(std::ceil(std::fabs(sweep) / std::max(step, 1e-3f)));
    return std::clamp(n, 2, 256);
}

void Dedupe(std::vector<Pt>& pts, bool closed, float eps) {
    std::vector<Pt> out;
    out.reserve(pts.size());
    for (const Pt& p : pts) {
        if (out.empty() || Len(Sub(p, out.back())) > eps) out.push_back(p);
    }
    if (closed) {
        while (out.size() > 1 && Len(Sub(out.front(), out.back())) <= eps) out.pop_back();
    }
    pts.swap(out);
}

float SignedArea(const std::vector<Pt>& r) {
    double a = 0;
    for (size_t i = 0; i < r.size(); ++i) {
        const Pt& p = r[i];
        const Pt& q = r[(i + 1) % r.size()];
        a += static_cast<double>(p.x) * q.y - static_cast<double>(q.x) * p.y;
    }
    return static_cast<float>(a * 0.5);
}

bool IsConvex(const std::vector<Pt>& r) {
    const size_t n = r.size();
    for (size_t i = 0; i < n; ++i) {
        const Pt a = Sub(r[(i + 1) % n], r[i]);
        const Pt b = Sub(r[(i + 2) % n], r[(i + 1) % n]);
        if (Cross(a, b) < -1e-4f) return false;
    }
    return true;
}

bool InsideTri(Pt p, Pt a, Pt b, Pt c) {
    const float d1 = Cross(Sub(b, a), Sub(p, a));
    const float d2 = Cross(Sub(c, b), Sub(p, b));
    const float d3 = Cross(Sub(a, c), Sub(p, c));
    return d1 >= 0 && d2 >= 0 && d3 >= 0;
}

// Triangulates a counter-clockwise ring whose vertices are mesh vertices base..base+n-1.
void Triangulate(Mesh& mesh, const std::vector<Pt>& ring, uint32_t base) {
    const size_t n = ring.size();
    if (IsConvex(ring)) {
        for (size_t i = 1; i + 1 < n; ++i) {
            mesh.Tri(base, base + static_cast<uint32_t>(i), base + static_cast<uint32_t>(i + 1));
        }
        return;
    }
    std::vector<uint32_t> poly(n);
    for (size_t i = 0; i < n; ++i) poly[i] = static_cast<uint32_t>(i);
    size_t guard = n * n + 8;
    while (poly.size() > 3 && guard-- > 0) {
        bool clipped = false;
        const size_t m = poly.size();
        for (size_t i = 0; i < m && !clipped; ++i) {
            const uint32_t ia = poly[(i + m - 1) % m], ib = poly[i], ic = poly[(i + 1) % m];
            const Pt a = ring[ia], b = ring[ib], c = ring[ic];
            if (Cross(Sub(b, a), Sub(c, b)) <= 1e-6f) continue;   // reflex or flat
            bool empty = true;
            for (size_t j = 0; j < m && empty; ++j) {
                const uint32_t k = poly[j];
                if (k == ia || k == ib || k == ic) continue;
                if (InsideTri(ring[k], a, b, c)) empty = false;
            }
            if (!empty) continue;
            mesh.Tri(base + ia, base + ib, base + ic);
            poly.erase(poly.begin() + static_cast<std::ptrdiff_t>(i));
            clipped = true;
        }
        if (!clipped) break;   // degenerate input: fall back to a fan over what is left
    }
    for (size_t i = 1; i + 1 < poly.size(); ++i) mesh.Tri(base + poly[0], base + poly[i], base + poly[i + 1]);
}

} // namespace

Mat Mat::Times(const Mat& n) const {
    Mat r;
    r.a = a * n.a + c * n.b;
    r.b = b * n.a + d * n.b;
    r.c = a * n.c + c * n.d;
    r.d = b * n.c + d * n.d;
    r.e = a * n.e + c * n.f + e;
    r.f = b * n.e + d * n.f + f;
    return r;
}

float Mat::Scale() const { return std::sqrt(std::fabs(a * d - b * c)); }
bool Mat::AxisAligned() const { return std::fabs(b) < 1e-6f && std::fabs(c) < 1e-6f; }

Mat Mat::Translate(float x, float y) { Mat m; m.e = x; m.f = y; return m; }
Mat Mat::ScaleBy(float sx, float sy) { Mat m; m.a = sx; m.d = sy; return m; }
Mat Mat::Rotate(float degrees) {
    const float r = degrees * kDegToRad;
    Mat m;
    m.a = std::cos(r); m.b = std::sin(r); m.c = -std::sin(r); m.d = std::cos(r);
    return m;
}

uint32_t ScaleAlpha(uint32_t argb, float factor) {
    const float a = static_cast<float>(argb >> 24) * std::clamp(factor, 0.0f, 1.0f);
    return (static_cast<uint32_t>(a + 0.5f) << 24) | (argb & 0x00FFFFFFu);
}

void BuildRoundRect(std::vector<Pt>& out, float x, float y, float w, float h, float r, const Mat& m) {
    out.clear();
    r = std::clamp(r, 0.0f, std::min(w, h) * 0.5f);
    if (r <= 0.0f) {
        out.push_back(m.Apply({x, y}));
        out.push_back(m.Apply({x + w, y}));
        out.push_back(m.Apply({x + w, y + h}));
        out.push_back(m.Apply({x, y + h}));
        return;
    }
    const int n = Segments(r * m.Scale(), kPi * 0.5f);
    const Pt centres[4] = {{x + r, y + r}, {x + w - r, y + r}, {x + w - r, y + h - r}, {x + r, y + h - r}};
    const float start[4] = {kPi, 1.5f * kPi, 0.0f, 0.5f * kPi};
    for (int corner = 0; corner < 4; ++corner) {
        for (int i = 0; i <= n; ++i) {
            const float t = start[corner] + (kPi * 0.5f) * static_cast<float>(i) / static_cast<float>(n);
            out.push_back(m.Apply({centres[corner].x + r * std::cos(t), centres[corner].y + r * std::sin(t)}));
        }
    }
}

void BuildCircle(std::vector<Pt>& out, float cx, float cy, float r, const Mat& m) {
    out.clear();
    const int n = std::max(8, Segments(r * m.Scale(), 2.0f * kPi));
    for (int i = 0; i < n; ++i) {
        const float t = 2.0f * kPi * static_cast<float>(i) / static_cast<float>(n);
        out.push_back(m.Apply({cx + r * std::cos(t), cy + r * std::sin(t)}));
    }
}

bool BuildArc(std::vector<Pt>& out, float cx, float cy, float r, float a0, float a1, const Mat& m) {
    out.clear();
    float sweep = a1 - a0;
    const bool full = std::fabs(sweep) >= 360.0f;
    if (full) sweep = sweep < 0 ? -360.0f : 360.0f;
    const int n = Segments(r * m.Scale(), sweep * kDegToRad);
    const int count = full ? n : n + 1;   // a full turn closes onto its first point
    for (int i = 0; i < count; ++i) {
        const float a = (a0 + sweep * static_cast<float>(i) / static_cast<float>(n)) * kDegToRad;
        out.push_back(m.Apply({cx + r * std::sin(a), cy - r * std::cos(a)}));
    }
    return full;
}

void AddFill(Mesh& mesh, std::vector<Pt> ring, uint32_t argb) {
    if ((argb >> 24) == 0) return;
    Dedupe(ring, true, 1e-3f);
    const size_t n = ring.size();
    if (n < 3) return;
    float area = SignedArea(ring);
    if (std::fabs(area) < 1e-4f) return;
    if (area < 0) {
        std::reverse(ring.begin(), ring.end());
        area = -area;
    }
    float perimeter = 0;
    for (size_t i = 0; i < n; ++i) perimeter += Len(Sub(ring[(i + 1) % n], ring[i]));
    // 2*area/perimeter is the inradius of a tangential polygon: how far the edge can move inward.
    const float thickness = perimeter > 0 ? 2.0f * area / perimeter : 0.0f;
    const float inset = std::min(0.5f, thickness * 0.8f);
    const float outset = 1.0f - inset;

    std::vector<Pt> inner(n);
    const uint32_t clear = Transparent(argb);
    const uint32_t base = static_cast<uint32_t>(mesh.v.size());
    std::vector<Pt> outer(n);
    for (size_t i = 0; i < n; ++i) {
        const Pt p0 = ring[(i + n - 1) % n], p1 = ring[i], p2 = ring[(i + 1) % n];
        const Pt t0 = Norm(Sub(p1, p0)), t1 = Norm(Sub(p2, p1));
        const Pt n0{t0.y, -t0.x}, n1{t1.y, -t1.x};
        Pt nn = Add(n0, n1);
        float miter = 1.0f;
        if (Len(nn) < 1e-3f) {
            nn = n0;
        } else {
            nn = Norm(nn);
            miter = std::min(1.0f / std::max(Dot(nn, n0), 0.01f), 3.0f);
        }
        inner[i] = Sub(p1, Mul(nn, inset * miter));
        outer[i] = Add(p1, Mul(nn, outset * miter));
    }
    for (size_t i = 0; i < n; ++i) mesh.Add(inner[i].x, inner[i].y, argb);
    for (size_t i = 0; i < n; ++i) mesh.Add(outer[i].x, outer[i].y, clear);
    Triangulate(mesh, inner, base);
    const uint32_t outerBase = base + static_cast<uint32_t>(n);
    for (size_t i = 0; i < n; ++i) {
        const uint32_t j = static_cast<uint32_t>((i + 1) % n), k = static_cast<uint32_t>(i);
        mesh.Quad(base + k, base + j, outerBase + j, outerBase + k);
    }
}

void AddStroke(Mesh& mesh, std::vector<Pt> pts, bool closed, float width, uint32_t argb, Cap cap) {
    Dedupe(pts, closed, 1e-3f);
    size_t n = pts.size();
    if (closed && n < 3) closed = false;
    if (n < 2 || width <= 0.0f || (argb >> 24) == 0) return;
    if (width < 1.0f) {            // a hairline: draw one pixel wide, scaled to the coverage it should have
        argb = ScaleAlpha(argb, width);
        width = 1.0f;
    }
    const float half = width * 0.5f;
    const float hi = half - 0.5f, ho = half + 0.5f;
    const uint32_t clear = Transparent(argb);

    const size_t segCount = closed ? n : n - 1;
    std::vector<Pt> tangent(segCount), left(segCount);
    for (size_t k = 0; k < segCount; ++k) {
        tangent[k] = Norm(Sub(pts[(k + 1) % n], pts[k]));
        left[k] = {-tangent[k].y, tangent[k].x};
    }
    const uint32_t base = static_cast<uint32_t>(mesh.v.size());
    for (size_t i = 0; i < n; ++i) {
        Pt nn;
        float miter = 1.0f;
        if (!closed && i == 0) {
            nn = left[0];
        } else if (!closed && i == n - 1) {
            nn = left[n - 2];
        } else {
            const Pt n0 = left[(i + segCount - 1) % segCount], n1 = left[i % segCount];
            nn = Add(n0, n1);
            if (Len(nn) < 1e-3f) {
                nn = n0;
            } else {
                nn = Norm(nn);
                miter = std::min(1.0f / std::max(Dot(nn, n0), 0.01f), 2.5f);
            }
        }
        const Pt p = pts[i];
        const float offsets[4] = {ho, hi, -hi, -ho};
        const uint32_t colours[4] = {clear, argb, argb, clear};
        for (int c = 0; c < 4; ++c) {
            mesh.Add(p.x + nn.x * miter * offsets[c], p.y + nn.y * miter * offsets[c], colours[c]);
        }
    }
    for (size_t k = 0; k < segCount; ++k) {
        const uint32_t r0 = base + static_cast<uint32_t>(k) * 4;
        const uint32_t r1 = base + static_cast<uint32_t>((k + 1) % n) * 4;
        for (uint32_t c = 0; c < 3; ++c) mesh.Quad(r0 + c, r0 + c + 1, r1 + c + 1, r1 + c);
    }
    if (!closed && cap == Cap::Round) {
        const int steps = std::clamp(static_cast<int>(std::ceil(kPi / (2.0f * std::acos(std::max(0.0f, 1.0f - 0.25f / ho))))), 4, 24);
        for (int end = 0; end < 2; ++end) {
            const Pt centre = end == 0 ? pts[0] : pts[n - 1];
            const Pt normal = end == 0 ? left[0] : left[n - 2];
            const Pt outward = end == 0 ? Pt{-tangent[0].x, -tangent[0].y} : tangent[n - 2];
            const uint32_t hub = mesh.Add(centre.x, centre.y, argb);
            uint32_t prevIn = 0, prevOut = 0;
            for (int i = 0; i <= steps; ++i) {
                const float t = kPi * static_cast<float>(i) / static_cast<float>(steps);
                const Pt dir = Add(Mul(normal, std::cos(t)), Mul(outward, std::sin(t)));
                const uint32_t in = mesh.Add(centre.x + dir.x * hi, centre.y + dir.y * hi, argb);
                const uint32_t out = mesh.Add(centre.x + dir.x * ho, centre.y + dir.y * ho, clear);
                if (i > 0) {
                    mesh.Tri(hub, prevIn, in);
                    mesh.Quad(prevIn, prevOut, out, in);
                }
                prevIn = in;
                prevOut = out;
            }
        }
    }
}

} // namespace hud
