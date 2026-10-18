#pragma once
// Software backend: rasterizes the renderer's triangles into an RGBA buffer. It
// is the reference the offline tests assert pixels against, and the reference the
// Direct3D 9 backend is compared to on a real device.
#include "Backend.h"

#include <vector>

namespace hud {

class SoftBackend final : public IBackend {
public:
    SoftBackend(int width, int height);

    void Clear(uint32_t argb);
    int Width() const { return w_; }
    int Height() const { return h_; }
    // Straight-alpha RGBA at (x,y); out of range reads as 0.
    void Pixel(int x, int y, uint8_t rgba[4]) const;
    const std::vector<uint8_t>& Pixels() const { return pix_; }

    bool BeginFrame(int& width, int& height) override;
    void EndFrame() override {}
    bool CreateTexture(int w, int h, const uint8_t* rgba, TextureRef& out) override;
    void UpdateTexture(int id, int x, int y, int w, int h, const uint8_t* rgba, int pitchBytes) override;
    void DestroyTexture(int id) override;
    bool Draw(const Vtx* vertices, int vertexCount, const uint16_t* indices, int indexCount, int texture) override;
    bool CaptureBackground() override;
    void ReleaseBackground() override { bg_.clear(); }
    bool HasBackground() const override { return !bg_.empty(); }
    void DrawBackground() override;

    int textureCount() const;

private:
    struct Tex {
        int w = 0, h = 0;
        std::vector<uint8_t> rgba;
        bool live = false;
    };
    int w_, h_;
    std::vector<uint8_t> pix_;
    std::vector<uint8_t> bg_;
    std::vector<Tex> textures_;   // id = index + 1

    void Triangle(const Vtx& a, const Vtx& b, const Vtx& c, const Tex* tex);
};

} // namespace hud
