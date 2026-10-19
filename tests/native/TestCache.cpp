#include "Kit.h"

#include <chrono>
#include <cstring>

using namespace hud;
using namespace kit;

// The renderer keeps the tessellated frame of the scene it draws and replays it at every Present until something it depends
// on changes. These tests count tessellations and replays (RenderStats) and compare pixels: a replay must look exactly like a
// fresh render, and every change that makes the frame stale must produce a fresh one.

namespace {

const std::vector<uint8_t> kRed = {255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255};
const std::vector<uint8_t> kGreen = {0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255};
const std::vector<uint8_t> kBlue = {0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255};

// A rectangle, a polygon, an image and a line of text: every kind of batch the renderer records.
const char* kScene =
    "{\"v\":1,\"w\":200,\"h\":120,\"background\":false,\"cmds\":["
    "{\"t\":\"rect\",\"x\":4,\"y\":4,\"w\":190,\"h\":110,\"fill\":\"#203040\",\"r\":8},"
    "{\"t\":\"poly\",\"pts\":[100,10,150,60,100,100,60,60],\"fill\":\"#c08020\",\"stroke\":\"#ffffff\",\"sw\":3,\"closed\":true},"
    "{\"t\":\"image\",\"id\":\"pic\",\"x\":10,\"y\":10,\"w\":40,\"h\":40},"
    "{\"t\":\"text\",\"x\":10,\"y\":100,\"s\":\"Cache 123\",\"size\":22,\"color\":\"#ffffff\"}]}";

std::shared_ptr<const Scene> Parse(const std::string& json) {
    Scene scene;
    SceneError error;
    if (!ParseScene(json, scene, error)) throw t::Failure{"scene refused: " + error.code + " " + error.detail};
    return std::make_shared<const Scene>(std::move(scene));
}

// Draws one frame on a cleared surface and returns its pixels.
std::vector<uint8_t> Frame(Fixture& f) {
    f.soft.Clear(0xFF000000u);
    if (f.renderer.Render(f.soft) != RenderResult::Drawn) throw t::Failure{"the frame was not drawn"};
    return f.soft.Pixels();
}

std::wstring AnotherFont() {
    for (const wchar_t* name : {L"cour.ttf", L"times.ttf", L"consola.ttf", L"verdana.ttf"}) {
        const std::wstring path = WindowsFont(name);
        if (!path.empty()) return path;
    }
    return L"";
}

} // namespace

TEST(cache_replay, "cache.the-same-scene-is-tessellated-once") {
    Fixture f(200, 120);
    RegisterTestFonts(f.fonts);
    CHECK(f.renderer.PutImage("pic", 2, 2, std::vector<uint8_t>(kRed)) == ImageResult::Ok);
    f.renderer.SetScene(Parse(kScene));
    CHECK(!f.renderer.HasFrameCache());
    const auto first = Frame(f);
    RenderStats s = f.renderer.Stats();
    CHECK_MSG(s.tessellations == 1 && s.replays == 0 && f.renderer.HasFrameCache(), "first frame: " + std::to_string(s.tessellations) + "/" + std::to_string(s.replays));
    for (int i = 0; i < 5; ++i) CHECK_MSG(Frame(f) == first, "replay " + std::to_string(i) + " differs from the tessellated frame");
    s = f.renderer.Stats();
    CHECK_MSG(s.tessellations == 1 && s.replays == 5 && s.framesDrawn == 6, std::to_string(s.tessellations) + "/" + std::to_string(s.replays) + "/" + std::to_string(s.framesDrawn));
    // The counters of a replay are those of the frame.
    CHECK(s.commandsDrawn == 6 * 4);
    CHECK(s.triangles % 6 == 0 && s.triangles > 0 && s.drawCalls % 6 == 0);
    // And the picture is right: red image, the polygon, text pixels.
    CHECK(Near(Px(f.soft, 20, 20), 255, 0, 0, 2));
    CHECK(Near(Px(f.soft, 100, 55), 0xc0, 0x80, 0x20, 4));
    CHECK(Changed(f.soft, 10, 80, 120, 112, {0x20, 0x30, 0x40, 255}, 80).count > 100);
}

TEST(cache_new_scene, "cache.a-new-scene-is-tessellated-again") {
    Fixture f(200, 120);
    RegisterTestFonts(f.fonts);
    f.renderer.PutImage("pic", 2, 2, std::vector<uint8_t>(kRed));
    f.renderer.SetScene(Parse(kScene));
    const auto first = Frame(f);
    Frame(f);
    CHECK(f.renderer.Stats().tessellations == 1);
    // Equal text, another scene object: the key is the object, so it is drawn from its commands again.
    f.renderer.SetScene(Parse(kScene));
    CHECK(Frame(f) == first);
    RenderStats s = f.renderer.Stats();
    CHECK_MSG(s.tessellations == 2 && s.replays == 1, std::to_string(s.tessellations) + "/" + std::to_string(s.replays));
    // A different scene shows its own content, not the cached one.
    f.renderer.SetScene(Parse(Doc("{\"t\":\"rect\",\"x\":0,\"y\":0,\"w\":200,\"h\":120,\"fill\":\"#00ff00\"}", 200, 120)));
    Frame(f);
    CHECK(Near(Px(f.soft, 100, 60), 0, 255, 0, 1));
    CHECK(f.renderer.Stats().tessellations == 3);
    Frame(f);
    CHECK(f.renderer.Stats().tessellations == 3 && f.renderer.Stats().replays == 2);
    // No scene, an empty scene: nothing is drawn and nothing is kept for them.
    f.renderer.SetScene(nullptr);
    f.soft.Clear(0xFF000000u);
    CHECK(f.renderer.Render(f.soft) == RenderResult::Nothing);
    f.renderer.SetScene(Parse(Doc("")));
    CHECK(f.renderer.Render(f.soft) == RenderResult::Nothing);
    // Going back to the earlier scene object tessellates it again (the cache holds one frame).
    f.renderer.SetScene(Parse(kScene));
    CHECK(Frame(f) == first && f.renderer.Stats().tessellations == 4);
}

TEST(cache_image, "cache.an-image-change-drop-or-late-arrival-is-seen") {
    Fixture f(200, 120);
    RegisterTestFonts(f.fonts);
    f.renderer.PutImage("pic", 2, 2, std::vector<uint8_t>(kRed));
    f.renderer.SetScene(Parse(kScene));
    Frame(f);
    Frame(f);
    CHECK(f.renderer.Stats().tessellations == 1 && f.renderer.Stats().replays == 1);
    CHECK(Near(Px(f.soft, 20, 20), 255, 0, 0, 2));

    // Replacing the picture under the same id: the next frame shows the new one.
    CHECK(f.renderer.PutImage("pic", 2, 2, std::vector<uint8_t>(kGreen)) == ImageResult::Ok);
    Frame(f);
    CHECK_MSG(Near(Px(f.soft, 20, 20), 0, 255, 0, 2), Describe(Px(f.soft, 20, 20)));
    CHECK(f.renderer.Stats().tessellations == 2);
    Frame(f);
    CHECK(f.renderer.Stats().tessellations == 2 && f.renderer.Stats().replays == 2);

    // Dropping it: the image is missing, the frame is drawn without it and is not kept (the picture may come back).
    CHECK(f.renderer.DropImage("pic"));
    Frame(f);
    CHECK(f.renderer.Stats().missingImages == 1 && !f.renderer.HasFrameCache());
    CHECK(Near(Px(f.soft, 20, 20), 0x20, 0x30, 0x40, 2));
    Frame(f);
    CHECK_MSG(f.renderer.Stats().tessellations == 4 && f.renderer.Stats().missingImages == 2, "a frame with a missing picture is built every time");

    // The picture arrives late: the very next frame has it, and is kept again.
    CHECK(f.renderer.PutImage("pic", 2, 2, std::vector<uint8_t>(kBlue)) == ImageResult::Ok);
    Frame(f);
    CHECK(Near(Px(f.soft, 20, 20), 0, 0, 255, 2) && f.renderer.HasFrameCache() && f.renderer.Stats().tessellations == 5);
    Frame(f);
    CHECK(f.renderer.Stats().tessellations == 5 && f.renderer.Stats().replays == 3);

    // An image the scene does not use is still an image-set change: the frame is rebuilt once, never wrong.
    f.renderer.PutImage("unused", 2, 2, std::vector<uint8_t>(kRed));
    const auto after = Frame(f);
    CHECK(f.renderer.Stats().tessellations == 6 && Near(Px(f.soft, 20, 20), 0, 0, 255, 2));
    CHECK(Frame(f) == after && f.renderer.Stats().tessellations == 6);
}

TEST(cache_fonts, "cache.a-font-change-is-seen") {
    Fixture f(200, 120);
    RegisterTestFonts(f.fonts);
    const std::wstring other = AnotherFont();
    if (other.empty()) SKIP("no second system font to swap in (cour.ttf, times.ttf, consola.ttf, verdana.ttf)");
    f.renderer.PutImage("pic", 2, 2, std::vector<uint8_t>(kRed));
    f.renderer.SetScene(Parse(kScene));
    const auto arial = Frame(f);
    CHECK(Frame(f) == arial && f.renderer.Stats().tessellations == 1 && f.renderer.Stats().replays == 1);
    const uint32_t generation = f.glyphs.Generation();

    std::string error;
    CHECK(f.fonts.Put(FontRole::Regular, other, error));
    CHECK_MSG(f.glyphs.Generation() != generation, "registering a font moves the generation");
    const auto changed = Frame(f);
    CHECK_MSG(f.renderer.Stats().tessellations == 2, "tessellated again after the font change: " + std::to_string(f.renderer.Stats().tessellations));
    CHECK_MSG(changed != arial, "the text is drawn with the new font");
    CHECK(Frame(f) == changed && f.renderer.Stats().tessellations == 2 && f.renderer.Stats().replays == 2);

    // Clearing the fonts (what the layer does at stop) is a change too.
    f.fonts.Clear();
    Frame(f);
    CHECK(f.renderer.Stats().tessellations == 3);
}

TEST(cache_backend, "cache.a-reset-a-new-device-or-released-resources-start-over") {
    Fixture f(200, 120);
    RegisterTestFonts(f.fonts);
    f.renderer.PutImage("pic", 2, 2, std::vector<uint8_t>(kRed));
    f.renderer.SetScene(Parse(kScene));
    const auto first = Frame(f);
    Frame(f);
    CHECK(f.renderer.Stats().tessellations == 1 && f.renderer.HasFrameCache());

    // The Reset hook of the layer.
    f.renderer.InvalidateFrameCache();
    CHECK(!f.renderer.HasFrameCache());
    CHECK(Frame(f) == first && f.renderer.Stats().tessellations == 2 && f.renderer.HasFrameCache());

    // The backend's textures are gone (device replaced).
    f.renderer.ForgetBackendResources();
    CHECK(!f.renderer.HasFrameCache());
    SoftBackend second(200, 120);
    second.Clear(0xFF000000u);
    CHECK(f.renderer.Render(second) == RenderResult::Drawn);
    CHECK(second.Pixels() == first && f.renderer.Stats().tessellations == 3);

    // Another backend object than the one the frame was tessellated on: its textures are not the ones the batches name.
    f.soft.Clear(0xFF000000u);
    CHECK(f.renderer.Render(f.soft) == RenderResult::Drawn);
    CHECK(f.soft.Pixels() == first && f.renderer.Stats().tessellations == 4);

    // Releasing the textures (layer stop) drops the frame as well.
    f.renderer.ReleaseBackendResources(f.soft);
    CHECK(!f.renderer.HasFrameCache());
    CHECK(Frame(f) == first && f.renderer.Stats().tessellations == 5);
}

TEST(cache_equals_fresh, "cache.a-replay-equals-a-fresh-render-of-every-golden-scene") {
    for (const char* name : {"scene-minimal.json", "scene-radar.json", "scene-pause.json", "radar-golden-small.json"}) {
        bool ok = false;
        const std::string json = t::ReadFile(t::GetConfig().vectorsDirectory + "/" + name, ok);
        if (!ok) SKIP("the golden vectors directory was not given (--vectors)");
        Fixture cached(1920, 1080), fresh(1920, 1080);
        RegisterTestFonts(cached.fonts);
        RegisterTestFonts(fresh.fonts);
        const std::vector<uint8_t> player = {255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255};
        for (const char* id : {"player", "stop"}) {
            cached.renderer.PutImage(id, 2, 2, std::vector<uint8_t>(player));
            fresh.renderer.PutImage(id, 2, 2, std::vector<uint8_t>(player));
        }
        const auto scene = Parse(json);
        cached.renderer.SetScene(scene);
        fresh.renderer.SetScene(scene);
        Frame(cached);
        const auto replayed = Frame(cached);
        CHECK_MSG(cached.renderer.Stats().replays == 1, std::string(name) + ": was replayed");
        // The fresh renderer tessellates a frame it has never seen, on its own surface.
        const auto built = Frame(fresh);
        CHECK_MSG(replayed == built, std::string(name) + ": the replay differs from a fresh render");
    }
}

TEST(cache_limits, "cache.a-frame-too-large-to-keep-is-built-every-time") {
    // Big circles: hundreds of thousands of vertices in one frame.
    std::string cmds;
    for (int i = 0; i < 1500; ++i) {
        if (i > 0) cmds += ",";
        cmds += "{\"t\":\"circle\",\"cx\":" + std::to_string(i % 60) + ",\"cy\":" + std::to_string(i % 40) + ",\"r\":900,\"fill\":\"#10101020\"}";
    }
    Fixture f(64, 48);
    f.renderer.SetScene(Parse(Doc(cmds, 64, 48)));
    Frame(f);
    Frame(f);
    const RenderStats s = f.renderer.Stats();
    CHECK_MSG(s.tessellations == 2 && s.replays == 0 && !f.renderer.HasFrameCache(), "too big to keep: " + std::to_string(s.tessellations) + "/" + std::to_string(s.replays));
}

TEST(cache_text_eviction, "cache.a-frame-that-evicted-glyphs-is-not-kept") {
    // More distinct large glyphs than the big atlas holds, in one frame: cells are reused while the frame is drawn, so the
    // quads of the early glyphs would sample other glyphs if the frame were replayed. Such a frame is built every time.
    Fixture f(400, 300);
    RegisterTestFonts(f.fonts);
    const std::string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    std::string cmds;
    for (int i = 0; i < 60; ++i) {
        if (i > 0) cmds += ",";
        std::string text;
        for (int k = 0; k < 40; ++k) text.push_back(alphabet[static_cast<size_t>((i * 7 + k) % 62)]);
        cmds += "{\"t\":\"text\",\"x\":2,\"y\":" + std::to_string(5 + i * 4) + ",\"s\":\"" + text + "\",\"size\":" + std::to_string(40 + i % 21) + ",\"color\":\"#ffffff\"}";
    }
    f.renderer.SetScene(Parse(Doc(cmds, 400, 300)));
    Frame(f);
    Frame(f);
    const RenderStats s = f.renderer.Stats();
    CHECK_MSG(s.atlasEvictions > 0, "the scene is meant to overflow the atlas: " + std::to_string(s.atlasEvictions));
    CHECK_MSG(s.replays == 0 && s.tessellations == 2 && !f.renderer.HasFrameCache(), "evicting frame: " + std::to_string(s.tessellations) + "/" + std::to_string(s.replays));
}

namespace {

// A backend that draws nothing: what is timed is the renderer's own work, not a rasterizer's.
class NullBackend final : public IBackend {
public:
    bool BeginFrame(int& w, int& h) override { w = 1920; h = 1080; return true; }
    void EndFrame() override {}
    bool CreateTexture(int, int, const uint8_t*, TextureRef& out) override { out.id = ++textures; return true; }
    void UpdateTexture(int, int, int, int, int, const uint8_t*, int) override {}
    void DestroyTexture(int) override {}
    bool Draw(const Vtx*, int vertexCount, const uint16_t*, int indexCount, int) override { vertices += static_cast<uint64_t>(vertexCount); indices += static_cast<uint64_t>(indexCount); return true; }
    bool CaptureBackground() override { return false; }
    void ReleaseBackground() override {}
    bool HasBackground() const override { return false; }
    void DrawBackground() override {}
    int textures = 0;
    uint64_t vertices = 0, indices = 0;
};

} // namespace

TEST(cache_cost, "cache.a-replay-costs-a-fraction-of-a-tessellation") {
    // A radar-sized scene: the ring, 1100 map polygons and polylines, markers and a few labels.
    std::string cmds = "{\"t\":\"circle\",\"cx\":1700,\"cy\":880,\"r\":160,\"fill\":\"#0b151ee6\",\"stroke\":\"#ffffff\",\"sw\":4},{\"t\":\"clip\",\"kind\":\"circle\",\"cx\":1700,\"cy\":880,\"r\":156}";
    unsigned seed = 12345;
    const auto next = [&]() { seed = seed * 1664525u + 1013904223u; return static_cast<int>((seed >> 8) % 300); };
    for (int i = 0; i < 1100; ++i) {
        const int x = 1560 + next() % 280, y = 740 + next() % 280;
        if (i % 2 == 0)
            cmds += ",{\"t\":\"poly\",\"pts\":[" + std::to_string(x) + "," + std::to_string(y) + "," + std::to_string(x + 30) + "," + std::to_string(y + 5) + "," + std::to_string(x + 25) + "," + std::to_string(y + 30) + "," + std::to_string(x - 5) + "," + std::to_string(y + 20) + "],\"fill\":\"#27503ae6\",\"closed\":true}";
        else
            cmds += ",{\"t\":\"poly\",\"pts\":[" + std::to_string(x) + "," + std::to_string(y) + "," + std::to_string(x + 40) + "," + std::to_string(y + 8) + "," + std::to_string(x + 70) + "," + std::to_string(y + 30) + "],\"stroke\":\"#aab2bde6\",\"sw\":3,\"closed\":false}";
    }
    cmds += ",{\"t\":\"unclip\"},{\"t\":\"text\",\"x\":1700,\"y\":1050,\"s\":\"400 m\",\"size\":14,\"color\":\"#ffffff\"}";
    Fixture f(8, 8);
    RegisterTestFonts(f.fonts);
    NullBackend backend;
    f.renderer.SetScene(Parse(Doc(cmds, 1920, 1080)));
    CHECK(f.renderer.Render(backend) == RenderResult::Drawn);   // warms the glyph atlas

    const auto time = [&](bool invalidate) {
        const auto started = std::chrono::steady_clock::now();
        for (int i = 0; i < 200; ++i) {
            if (invalidate) f.renderer.InvalidateFrameCache();
            if (f.renderer.Render(backend) != RenderResult::Drawn) throw t::Failure{"frame not drawn"};
        }
        return std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - started).count() / 200;
    };
    const double tessellated = time(true);
    {
        // Where the time goes: the same scene with and without the circular clip around the map.
        std::string noClip = cmds;
        for (const char* piece : {",{\"t\":\"clip\",\"kind\":\"circle\",\"cx\":1700,\"cy\":880,\"r\":156}", ",{\"t\":\"unclip\"}"}) noClip.erase(noClip.find(piece), std::strlen(piece));
        f.renderer.SetScene(Parse(Doc(noClip, 1920, 1080)));
        const double plain = time(true);
        std::printf("  without the clip: tessellated %.3f ms\n", plain);
        f.renderer.SetScene(Parse(Doc(cmds, 1920, 1080)));
        f.renderer.Render(backend);
    }
    const uint64_t before = backend.vertices;
    const RenderStats s0 = f.renderer.Stats();
    const double replayed = time(false);
    std::printf("  one frame: tessellated %.3f ms, replayed %.3f ms (%.1fx)\n", tessellated, replayed, tessellated / std::max(replayed, 1e-6));
    CHECK_MSG(replayed < tessellated / 3, "replay " + std::to_string(replayed) + " ms vs tessellation " + std::to_string(tessellated) + " ms");
    CHECK_MSG(backend.vertices - before > 0, "the replay still sends the vertices to the backend");
    const RenderStats s1 = f.renderer.Stats();
    CHECK_MSG(s1.tessellations == s0.tessellations && s1.replays - s0.replays == 200, std::to_string(s1.tessellations - s0.tessellations) + "/" + std::to_string(s1.replays - s0.replays));
}
