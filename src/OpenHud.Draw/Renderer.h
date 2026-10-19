#pragma once
// The scene renderer: interprets a Scene (transforms, clips, shapes, text,
// images) into triangles and hands them to an IBackend. It owns the images and the
// glyph atlases. Scene and image updates may arrive from any thread; Render runs on
// the thread that owns the backend.
#include "Atlas.h"
#include "Backend.h"
#include "Fonts.h"
#include "Geometry.h"
#include "Scene.h"

#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

namespace hud {

constexpr size_t kMaxTotalImageBytes = 32u << 20;   // across all images (protects the 32-bit host)

struct RenderStats {
    uint64_t framesDrawn = 0;
    uint64_t commandsDrawn = 0;
    uint64_t unknownCommands = 0;   // of the scenes drawn (summed per frame)
    uint64_t skippedCommands = 0;
    uint64_t missingImages = 0;
    uint64_t glyphsDrawn = 0;
    uint64_t glyphFailures = 0;
    uint64_t atlasEvictions = 0;
    uint64_t triangles = 0;
    uint64_t drawCalls = 0;
    uint64_t tessellations = 0;   // frames built from the scene's commands (a new scene, or the cache was invalidated)
    uint64_t replays = 0;         // frames drawn again from the batches the last tessellation produced
};

enum class RenderResult { Drawn, Nothing, Skipped, Failed };
enum class ImageResult { Ok, Invalid, TooMany, TooBig, OutOfMemoryBudget };

class Renderer {
public:
    explicit Renderer(IGlyphSource& glyphs);

    // Thread safe.
    void SetScene(std::shared_ptr<const Scene> scene);
    std::shared_ptr<const Scene> CurrentScene() const;
    ImageResult PutImage(const std::string& id, int w, int h, std::vector<uint8_t>&& rgba);
    bool DropImage(const std::string& id);
    int ImageCount() const;
    RenderStats Stats() const;

    // Render thread.
    RenderResult Render(IBackend& backend);
    // The backend's textures no longer exist (device replaced): forget handles, rebuild on demand.
    void ForgetBackendResources();
    // Drops the tessellated frame: the next Render builds it again from the scene. Called when the device is reset;
    // a new scene, a changed or dropped image, a font change and a new view size do the same on their own.
    void InvalidateFrameCache();
    // Test access: the circle-clip shortcuts (inside the inscribed disc, outside the circumscribed one) can be switched off to
    // compare their output with the general clipping of every triangle.
    void SetCircleClipShortcuts(bool on) { circleShortcuts_ = on; }
    // Test access: whether a tessellated frame is being kept.
    bool HasFrameCache() const { return baked_ != nullptr; }
    // Destroys every texture this renderer created on the backend.
    void ReleaseBackendResources(IBackend& backend);

    int GlyphAtlasEvictions() const;
    // Test access: atlas cells in use.
    int GlyphsResident() const;

private:
    struct ImageData {
        int w = 0, h = 0;
        std::shared_ptr<const std::vector<uint8_t>> rgba;
        uint64_t version = 0;
    };
    struct BackendImage {
        TextureRef tex;
        uint64_t version = 0;
    };
    struct GlyphInfo {
        float advance = 0;
        int w = 0, h = 0, left = 0, top = 0;
        int atlas = -1;   // -1: nothing to draw
        int cell = -1;
    };
    struct Atlas {
        explicit Atlas(int cell) : lru(cell, 1024 / cell, 1024 / cell) {}
        LruAtlas lru;
        TextureRef tex;
    };
    // One draw call of a frame, as it went to the backend.
    struct BakedBatch {
        std::vector<Vtx> v;
        std::vector<uint16_t> i;
        int texture = 0;
    };
    // The frame of a scene as triangles. The scene is drawn at every Present but changes a few times a second, so the
    // commands are tessellated once and the batches are replayed until something they depend on changes.
    struct BakedFrame {
        std::shared_ptr<const Scene> scene;   // held: the key is the scene object itself, so an address is never reused
        const IBackend* backend = nullptr;
        int viewW = 0, viewH = 0;
        uint32_t fonts = 0;                   // glyph source generation: the atlas cells the quads point to
        uint64_t images = 0;                  // image set version: the textures the quads point to
        std::vector<BakedBatch> batches;
        size_t vertices = 0;
        uint64_t triangles = 0;
    };
    struct ClipPoly {
        std::vector<Pt> pts;   // convex, counter-clockwise in math orientation
        float minx = 0, miny = 0, maxx = 0, maxy = 0;
        bool axisRect = false;
        // A circular clip under a transform that keeps circles round: triangles are tested against the circle before any edge.
        bool circle = false;
        float ccx = 0, ccy = 0;     // centre, device pixels
        float rOut2 = 0;            // squared radius of a disc that contains the clip polygon
        float rIn2 = 0;             // squared radius of a disc that the clip polygon contains
    };

    IGlyphSource& glyphs_;
    mutable std::mutex lock_;
    std::shared_ptr<const Scene> scene_;
    std::map<std::string, ImageData> images_;
    size_t imageBytes_ = 0;
    uint64_t imageVersion_ = 0;
    uint64_t imagesSynced_ = ~0ull;
    RenderStats stats_;

    // Render-thread state.
    IBackend* backend_ = nullptr;
    std::map<std::string, BackendImage> backendImages_;
    std::vector<std::unique_ptr<Atlas>> atlases_;
    std::unordered_map<uint64_t, GlyphInfo> glyphInfo_;
    uint32_t fontGeneration_ = 0;
    std::map<uint64_t, FaceMetrics> metrics_;
    Mat current_;
    std::vector<Mat> stack_;
    std::vector<ClipPoly> clips_;
    Mesh mesh_;
    bool circleShortcuts_ = true;
    mutable std::vector<Vtx> clipNext_;   // scratch of ClipPolygon: reused, not allocated per triangle
    std::vector<Vtx> batchV_;
    std::vector<uint16_t> batchI_;
    int batchTexture_ = 0;
    bool failed_ = false;
    RenderStats frame_;
    std::unique_ptr<BakedFrame> baked_;     // the frame that can be replayed
    std::unique_ptr<BakedFrame> building_;  // the frame being tessellated now (flushed batches are copied into it)

    bool CanReplay(const std::shared_ptr<const Scene>& scene, const IBackend& backend, int viewW, int viewH, uint32_t fonts, uint64_t images) const;
    bool Replay(IBackend& backend);
    void SyncImages();
    void Flush();
    void Submit(const Mesh& mesh, int texture);
    void AppendTriangle(const Vtx& a, const Vtx& b, const Vtx& c, int texture);
    void SubmitClipped(const Mesh& mesh, int texture);
    bool ClipPolygon(std::vector<Vtx>& poly) const;
    void PushClip(const Cmd& cmd);
    void DrawCommand(const Cmd& cmd);
    void DrawShape(const std::vector<Pt>& ring, bool closed, const Cmd& cmd, float strokeWidth);
    void DrawText(const Cmd& cmd);
    void DrawImage(const Cmd& cmd);
    const GlyphInfo* Glyph(FontRole role, int px, uint32_t codepoint);
    Atlas* AtlasFor(int index);
    const FaceMetrics* MetricsFor(FontRole role, int px);
};

// UTF-8 to code points (invalid bytes become U+FFFD).
std::vector<uint32_t> DecodeUtf8(const std::string& text);

} // namespace hud
