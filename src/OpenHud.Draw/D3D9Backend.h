#pragma once
// Direct3D 9 backend: draws the renderer's triangles on the game's own device.
// It saves and restores exactly the device state it changes (never a state block),
// keeps its Direct3D default-pool objects (dynamic vertex/index buffers, the pause
// background render target) releasable for Reset, and keeps images and glyph
// atlases in the managed pool so they survive a Reset untouched.
#include "Backend.h"

#include <atomic>
#include <string>
#include <vector>

struct IDirect3DDevice9;
struct IDirect3DTexture9;
struct IDirect3DVertexBuffer9;
struct IDirect3DIndexBuffer9;

namespace hud {

class D3D9Backend final : public IBackend {
public:
    D3D9Backend();
    ~D3D9Backend() override;

    // Binds the backend to a device. A different device than before drops everything first.
    void Attach(IDirect3DDevice9* device);
    // Releases every Direct3D object (including managed textures) and forgets the device.
    void Detach();
    IDirect3DDevice9* Device() const { return device_; }
    // Releases the default-pool objects; call before the device is reset.
    void ReleaseDefaultPool();

    uint64_t SkippedFrames() const { return skipped_.load(); }
    // Size of the target of the last frame that began (0 before the first); read from other threads.
    int ViewWidth() const { return viewWidth_.load(); }
    int ViewHeight() const { return viewHeight_.load(); }
    const std::string& LastError() const { return lastError_; }

    bool BeginFrame(int& width, int& height) override;
    void EndFrame() override;
    bool CreateTexture(int w, int h, const uint8_t* rgba, TextureRef& out) override;
    void UpdateTexture(int id, int x, int y, int w, int h, const uint8_t* rgba, int pitchBytes) override;
    void DestroyTexture(int id) override;
    bool Draw(const Vtx* vertices, int vertexCount, const uint16_t* indices, int indexCount, int texture) override;
    bool CaptureBackground() override;
    void ReleaseBackground() override;
    bool HasBackground() const override { return background_ != nullptr; }
    void DrawBackground() override;

private:
    struct Entry {
        IDirect3DTexture9* texture = nullptr;
        int width = 0, height = 0;   // of the image; the texture is padded to powers of two
    };
    struct Saved;

    IDirect3DDevice9* device_ = nullptr;
    IDirect3DVertexBuffer9* vertices_ = nullptr;
    IDirect3DIndexBuffer9* indices_ = nullptr;
    IDirect3DTexture9* background_ = nullptr;
    std::vector<Entry> textures_;   // id = index + 1
    std::vector<int> freeIds_;
    Saved* saved_ = nullptr;
    int viewW_ = 0, viewH_ = 0;
    std::atomic<int> viewWidth_{0}, viewHeight_{0};
    bool sceneBegun_ = false;
    bool textured_ = false;
    bool stateKnown_ = false;
    std::atomic<uint64_t> skipped_{0};
    std::string lastError_;

    bool EnsureBuffers();
    void SetTextureMode(bool textured);
    bool DrawRaw(const Vtx* vertices, int vertexCount, const uint16_t* indices, int indexCount, IDirect3DTexture9* texture, bool opaque);
    void Fail(const char* what, long hr);
};

} // namespace hud
