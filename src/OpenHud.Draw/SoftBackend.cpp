#include "SoftBackend.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace hud {

namespace {

constexpr int kSub = 256;   // sub-pixel grid of the edge functions

struct Fixed {
    int64_t x, y;
};

Fixed ToFixed(const Vtx& v) {
    return {static_cast<int64_t>(std::llround(static_cast<double>(v.x) * kSub)),
            static_cast<int64_t>(std::llround(static_cast<double>(v.y) * kSub))};
}

int64_t Edge(const Fixed& a, const Fixed& b, int64_t px, int64_t py) {
    return (b.x - a.x) * (py - a.y) - (b.y - a.y) * (px - a.x);
}

// Top-left rule for a triangle with positive area (in this y-down fixed space).
bool TopLeft(const Fixed& a, const Fixed& b) {
    const int64_t dx = b.x - a.x, dy = b.y - a.y;
    return (dy == 0 && dx > 0) || dy < 0;
}

} // namespace

SoftBackend::SoftBackend(int width, int height) : w_(width), h_(height), pix_(static_cast<size_t>(width) * static_cast<size_t>(height) * 4, 0) {}

void SoftBackend::Clear(uint32_t argb) {
    for (size_t i = 0; i < pix_.size(); i += 4) {
        pix_[i] = static_cast<uint8_t>(argb >> 16);
        pix_[i + 1] = static_cast<uint8_t>(argb >> 8);
        pix_[i + 2] = static_cast<uint8_t>(argb);
        pix_[i + 3] = static_cast<uint8_t>(argb >> 24);
    }
}

void SoftBackend::Pixel(int x, int y, uint8_t rgba[4]) const {
    if (x < 0 || y < 0 || x >= w_ || y >= h_) {
        std::memset(rgba, 0, 4);
        return;
    }
    std::memcpy(rgba, &pix_[(static_cast<size_t>(y) * static_cast<size_t>(w_) + static_cast<size_t>(x)) * 4], 4);
}

bool SoftBackend::BeginFrame(int& width, int& height) {
    width = w_;
    height = h_;
    return true;
}

bool SoftBackend::CreateTexture(int w, int h, const uint8_t* rgba, TextureRef& out) {
    if (w <= 0 || h <= 0) return false;
    Tex t;
    t.w = w;
    t.h = h;
    t.rgba.assign(static_cast<size_t>(w) * static_cast<size_t>(h) * 4, 0);
    if (rgba != nullptr) std::memcpy(t.rgba.data(), rgba, t.rgba.size());
    t.live = true;
    textures_.push_back(std::move(t));
    out.id = static_cast<int>(textures_.size());
    out.u1 = out.v1 = 1.0f;
    return true;
}

void SoftBackend::UpdateTexture(int id, int x, int y, int w, int h, const uint8_t* rgba, int pitchBytes) {
    if (id < 1 || id > static_cast<int>(textures_.size()) || !textures_[static_cast<size_t>(id - 1)].live) return;
    Tex& t = textures_[static_cast<size_t>(id - 1)];
    for (int row = 0; row < h; ++row) {
        const int ty = y + row;
        if (ty < 0 || ty >= t.h) continue;
        for (int col = 0; col < w; ++col) {
            const int tx = x + col;
            if (tx < 0 || tx >= t.w) continue;
            std::memcpy(&t.rgba[(static_cast<size_t>(ty) * static_cast<size_t>(t.w) + static_cast<size_t>(tx)) * 4],
                        rgba + static_cast<size_t>(row) * static_cast<size_t>(pitchBytes) + static_cast<size_t>(col) * 4, 4);
        }
    }
}

void SoftBackend::DestroyTexture(int id) {
    if (id < 1 || id > static_cast<int>(textures_.size())) return;
    Tex& t = textures_[static_cast<size_t>(id - 1)];
    t.live = false;
    t.rgba.clear();
    t.rgba.shrink_to_fit();
}

int SoftBackend::textureCount() const {
    int n = 0;
    for (const Tex& t : textures_) n += t.live ? 1 : 0;
    return n;
}

bool SoftBackend::Draw(const Vtx* vertices, int vertexCount, const uint16_t* indices, int indexCount, int texture) {
    const Tex* tex = nullptr;
    if (texture > 0) {
        if (texture > static_cast<int>(textures_.size()) || !textures_[static_cast<size_t>(texture - 1)].live) return false;
        tex = &textures_[static_cast<size_t>(texture - 1)];
    }
    for (int i = 0; i + 2 < indexCount; i += 3) {
        if (indices[i] >= vertexCount || indices[i + 1] >= vertexCount || indices[i + 2] >= vertexCount) return false;
        Triangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]], tex);
    }
    return true;
}

void SoftBackend::Triangle(const Vtx& va, const Vtx& vb, const Vtx& vc, const Tex* tex) {
    Fixed a = ToFixed(va), b = ToFixed(vb), c = ToFixed(vc);
    const Vtx* p0 = &va;
    const Vtx* p1 = &vb;
    const Vtx* p2 = &vc;
    int64_t area = Edge(a, b, c.x, c.y);
    if (area == 0) return;
    if (area < 0) {           // make the winding positive; culling is off
        std::swap(b, c);
        std::swap(p1, p2);
        area = -area;
    }
    const int64_t minX = std::min({a.x, b.x, c.x}), maxX = std::max({a.x, b.x, c.x});
    const int64_t minY = std::min({a.y, b.y, c.y}), maxY = std::max({a.y, b.y, c.y});
    // Pixel centres are at (i + 0.5) * kSub.
    const int x0 = std::max(0, static_cast<int>((minX - kSub / 2 + kSub - 1) / kSub) - 1);
    const int x1 = std::min(w_ - 1, static_cast<int>(maxX / kSub) + 1);
    const int y0 = std::max(0, static_cast<int>((minY - kSub / 2 + kSub - 1) / kSub) - 1);
    const int y1 = std::min(h_ - 1, static_cast<int>(maxY / kSub) + 1);
    if (x0 > x1 || y0 > y1) return;

    const bool tl0 = TopLeft(b, c), tl1 = TopLeft(c, a), tl2 = TopLeft(a, b);
    const double invArea = 1.0 / static_cast<double>(area);

    const auto channel = [](uint32_t argb, int shift) { return static_cast<float>((argb >> shift) & 0xFF); };
    const float ca[4] = {channel(p0->color, 24), channel(p0->color, 16), channel(p0->color, 8), channel(p0->color, 0)};
    const float cb[4] = {channel(p1->color, 24), channel(p1->color, 16), channel(p1->color, 8), channel(p1->color, 0)};
    const float cc[4] = {channel(p2->color, 24), channel(p2->color, 16), channel(p2->color, 8), channel(p2->color, 0)};

    for (int y = y0; y <= y1; ++y) {
        const int64_t py = static_cast<int64_t>(y) * kSub + kSub / 2;
        for (int x = x0; x <= x1; ++x) {
            const int64_t px = static_cast<int64_t>(x) * kSub + kSub / 2;
            const int64_t e0 = Edge(b, c, px, py), e1 = Edge(c, a, px, py), e2 = Edge(a, b, px, py);
            if (e0 < 0 || e1 < 0 || e2 < 0) continue;
            if ((e0 == 0 && !tl0) || (e1 == 0 && !tl1) || (e2 == 0 && !tl2)) continue;
            const float w0 = static_cast<float>(static_cast<double>(e0) * invArea);
            const float w1 = static_cast<float>(static_cast<double>(e1) * invArea);
            const float w2 = static_cast<float>(static_cast<double>(e2) * invArea);
            float col[4];
            for (int k = 0; k < 4; ++k) col[k] = ca[k] * w0 + cb[k] * w1 + cc[k] * w2;   // A R G B
            if (tex != nullptr) {
                const float u = p0->u * w0 + p1->u * w1 + p2->u * w2;
                const float v = p0->v * w0 + p1->v * w1 + p2->v * w2;
                const float fx = u * static_cast<float>(tex->w) - 0.5f, fy = v * static_cast<float>(tex->h) - 0.5f;
                const int ix = static_cast<int>(std::floor(fx)), iy = static_cast<int>(std::floor(fy));
                const float tx = fx - static_cast<float>(ix), ty = fy - static_cast<float>(iy);
                float texel[4] = {0, 0, 0, 0};   // R G B A
                for (int dy = 0; dy < 2; ++dy) {
                    for (int dx = 0; dx < 2; ++dx) {
                        const int sx = std::clamp(ix + dx, 0, tex->w - 1), sy = std::clamp(iy + dy, 0, tex->h - 1);
                        const float weight = (dx ? tx : 1 - tx) * (dy ? ty : 1 - ty);
                        const uint8_t* s = &tex->rgba[(static_cast<size_t>(sy) * static_cast<size_t>(tex->w) + static_cast<size_t>(sx)) * 4];
                        for (int k = 0; k < 4; ++k) texel[k] += static_cast<float>(s[k]) * weight;
                    }
                }
                col[0] *= texel[3] / 255.0f;
                col[1] *= texel[0] / 255.0f;
                col[2] *= texel[1] / 255.0f;
                col[3] *= texel[2] / 255.0f;
            }
            const float alpha = std::clamp(col[0], 0.0f, 255.0f) / 255.0f;
            if (alpha <= 0.0f) continue;
            uint8_t* d = &pix_[(static_cast<size_t>(y) * static_cast<size_t>(w_) + static_cast<size_t>(x)) * 4];
            for (int k = 0; k < 3; ++k) {
                const float src = std::clamp(col[1 + k], 0.0f, 255.0f);
                d[k] = static_cast<uint8_t>(std::lround(src * alpha + static_cast<float>(d[k]) * (1.0f - alpha)));
            }
            d[3] = static_cast<uint8_t>(std::lround(255.0f * alpha + static_cast<float>(d[3]) * (1.0f - alpha)));
        }
    }
}

bool SoftBackend::CaptureBackground() {
    bg_ = pix_;
    return true;
}

void SoftBackend::DrawBackground() {
    if (bg_.empty()) return;
    pix_ = bg_;   // same size as the target: the capture is the whole frame
}

} // namespace hud
