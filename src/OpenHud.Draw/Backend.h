#pragma once
// The renderer's view of a drawing surface. The renderer turns a scene into
// triangles with straight-alpha ARGB vertex colours (anti-aliasing is geometry:
// edge fringes whose alpha ramps to zero); a backend only draws them, so the
// software backend used by the offline tests and the Direct3D 9 backend of the
// game run exactly the same geometry.
#include <cstdint>

namespace hud {

struct Vtx {
    float x, y;       // device pixels, y down; a pixel (i,j) covers [i,i+1) x [j,j+1)
    float u, v;       // texture coordinates in [0,1] over the texture's usable area
    uint32_t color;   // 0xAARRGGBB, straight alpha
};

struct TextureRef {
    int id = 0;          // 0 = no texture
    float u1 = 1, v1 = 1; // fraction of the texture that holds the image (power-of-two padding)
};

class IBackend {
public:
    virtual ~IBackend() = default;

    // Starts a frame on the current target and reports its size in pixels.
    // False skips the frame (device lost or not ready); the backend counts it.
    virtual bool BeginFrame(int& width, int& height) = 0;
    virtual void EndFrame() = 0;

    // Straight-alpha RGBA, w*h*4 bytes (nullptr = transparent black).
    virtual bool CreateTexture(int w, int h, const uint8_t* rgba, TextureRef& out) = 0;
    virtual void UpdateTexture(int id, int x, int y, int w, int h, const uint8_t* rgba, int pitchBytes) = 0;
    virtual void DestroyTexture(int id) = 0;

    // Alpha-blended triangle list; texture 0 draws the vertex colour alone,
    // otherwise the vertex colour modulates the texture.
    virtual bool Draw(const Vtx* vertices, int vertexCount, const uint16_t* indices, int indexCount, int texture) = 0;

    // The pause background: a copy of the frame the game had when it was captured.
    virtual bool CaptureBackground() = 0;
    virtual void ReleaseBackground() = 0;
    virtual bool HasBackground() const = 0;
    virtual void DrawBackground() = 0;
};

} // namespace hud
