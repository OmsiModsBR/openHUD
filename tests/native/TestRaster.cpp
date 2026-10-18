#include "Kit.h"

#include <chrono>
#include <cstring>
#include <fstream>

using namespace hud;
using namespace kit;

namespace {

const char* kWhite = "\"#ffffff\"";

std::string Vector(const char* name) {
    bool ok = false;
    const std::string text = t::ReadFile(t::GetConfig().vectorsDirectory + "/" + name, ok);
    if (!ok) SKIP("the golden vectors directory was not given (--vectors)");
    return text;
}

std::string Rect(float x, float y, float w, float h, const char* fill = "#ffffff", float r = 0) {
    return "{\"t\":\"rect\",\"x\":" + std::to_string(x) + ",\"y\":" + std::to_string(y) + ",\"w\":" + std::to_string(w) + ",\"h\":" + std::to_string(h) +
           ",\"fill\":\"" + fill + "\",\"r\":" + std::to_string(r) + "}";
}

std::string Text(const std::string& s, float x, float y, float size, const char* extra = "") {
    return "{\"t\":\"text\",\"x\":" + std::to_string(x) + ",\"y\":" + std::to_string(y) + ",\"s\":\"" + s + "\",\"size\":" + std::to_string(size) +
           ",\"color\":" + kWhite + extra + "}";
}

} // namespace

TEST(raster_vector_minimal, "raster.vector-minimal") {
    Fixture f(1920, 1080);
    RegisterTestFonts(f.fonts);
    f.Run(Vector("scene-minimal.json"), 0xFF808080u);
    // Inside the translucent panel: #14171c at alpha 0xd9 over mid grey.
    const Rgba inside = Px(f.soft, 150, 100);
    CHECK_MSG(Near(inside, 36, 39, 43, 2), Describe(inside));
    CHECK(Near(Px(f.soft, 5, 5), 128, 128, 128, 0));
    // The panel is pixel aligned: its edge is exact, its rounded corner is cut.
    CHECK(Near(Px(f.soft, 9, 100), 128, 128, 128, 0) && Near(Px(f.soft, 10, 100), 36, 39, 43, 2));
    CHECK(Near(Px(f.soft, 309, 100), 36, 39, 43, 2) && Near(Px(f.soft, 310, 100), 128, 128, 128, 0));
    CHECK(Near(Px(f.soft, 150, 19), 128, 128, 128, 0) && Near(Px(f.soft, 150, 20), 36, 39, 43, 2));
    CHECK(Near(Px(f.soft, 150, 139), 36, 39, 43, 2) && Near(Px(f.soft, 150, 140), 128, 128, 128, 0));
    CHECK(Near(Px(f.soft, 10, 20), 128, 128, 128, 0));   // outside the 12 px corner arc
    CHECK(Near(Px(f.soft, 14, 24), 36, 39, 43, 2));      // inside it
    // Text: white pixels on the panel, left of x=24 nothing, all within the line box.
    const Box text = Changed(f.soft, 22, 26, 300, 58, {36, 39, 43, 255}, 60);
    CHECK_MSG(text.count > 150, std::to_string(text.count));
    CHECK(text.x0 >= 22 && text.x1 < 160 && text.y0 >= 26 && text.y1 <= 54);
    // Vertically centred on y=40 ("base":"mid").
    CHECK_MSG(std::abs((text.y0 + text.y1) / 2 - 40) <= 6, std::to_string(text.y0) + ".." + std::to_string(text.y1));
}

TEST(raster_vector_pause, "raster.vector-pause") {
    Fixture f(1920, 1080);
    RegisterTestFonts(f.fonts);
    // The game's frame (red) is captured as the pause background, then the surface is repainted green:
    // the layer must show the captured frame, not what the surface holds now.
    f.soft.Clear(0xFFC80000u);
    CHECK(f.soft.CaptureBackground());
    f.soft.Clear(0xFF00FF00u);
    f.Load(Vector("scene-pause.json"));
    CHECK(f.Draw() == RenderResult::Drawn);
    const Rgba dim = Px(f.soft, 100, 100);   // red at 65% black
    CHECK_MSG(Near(dim, 70, 0, 0, 2), Describe(dim));
    const Rgba button = Px(f.soft, 780, 430);
    CHECK_MSG(Near(button, 0, 201, 74, 1), Describe(button));
    CHECK(Near(Px(f.soft, 759, 430), 70, 0, 0, 2) && Near(Px(f.soft, 760, 430), 0, 201, 74, 1));
    const Box label = Changed(f.soft, 880, 430, 1040, 470, {0, 201, 74, 255}, 80);
    CHECK_MSG(label.count > 60, std::to_string(label.count));
    CHECK(std::abs((label.x0 + label.x1) / 2 - 960) <= 6);   // centred on x=960
    // Without a captured background the flag draws nothing extra: the scene sits over the surface.
    f.soft.ReleaseBackground();
    f.soft.Clear(0xFF00FF00u);
    CHECK(f.Draw() == RenderResult::Drawn);
    CHECK(Near(Px(f.soft, 100, 100), 0, 89, 0, 2));
}

TEST(raster_vector_radar, "raster.vector-radar") {
    Fixture f(1920, 1080);
    // The 2x2 image behind the "player" id: red, green / blue, white.
    const std::vector<uint8_t> pixels = {255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255};
    CHECK(f.renderer.PutImage("player", 2, 2, std::vector<uint8_t>(pixels)) == ImageResult::Ok);
    f.Run(Vector("scene-radar.json"), 0xFF808080u);
    // The disc: #0b151e at alpha 0xe6 over grey.
    const Rgba disc = Px(f.soft, 1560, 880);
    CHECK_MSG(Near(disc, 22, 32, 40, 2), Describe(disc));
    // The white ring (r 160, width 4) at 90 degrees, away from the arc.
    CHECK(Near(Px(f.soft, 1859, 880), 255, 255, 255, 12));
    CHECK(Near(Px(f.soft, 1870, 880), 128, 128, 128, 0));   // outside the disc
    // The green arc sits on top of the ring's inner edge.
    const Rgba arc = Px(f.soft, 1700, 722);
    CHECK_MSG(Near(arc, 0, 201, 74, 3), Describe(arc));
    // The polyline (#5588c7) is drawn, rotated, and never leaves the clip circle of radius 156.
    int stroke = 0, outside = 0;
    for (int y = 700; y < 1060; ++y) {
        for (int x = 1520; x < 1880; ++x) {
            if (!Near(Px(f.soft, x, y), 0x55, 0x88, 0xc7, 6)) continue;
            ++stroke;
            const double dx = x + 0.5 - 1700, dy = y + 0.5 - 880;
            if (std::sqrt(dx * dx + dy * dy) > 157.0) ++outside;
        }
    }
    CHECK_MSG(stroke > 600 && outside == 0, std::to_string(stroke) + "/" + std::to_string(outside));
    // The player image (2x2 magnified to 32x32): corners show the four colours.
    CHECK(Px(f.soft, 1686, 866).r > 230 && Px(f.soft, 1686, 866).g < 40);
    CHECK(Px(f.soft, 1713, 866).g > 230 && Px(f.soft, 1713, 866).r < 40);
    CHECK(Px(f.soft, 1686, 893).b > 230 && Px(f.soft, 1686, 893).r < 40);
    CHECK(Near(Px(f.soft, 1713, 893), 255, 255, 255, 12));
    CHECK(f.renderer.Stats().missingImages == 0);
}

TEST(raster_alpha_blend, "raster.alpha-blending") {
    Fixture f(100, 100);
    f.Run(Doc(Rect(10, 10, 40, 40, "#ff000080") + "," + Rect(30, 30, 40, 40, "#0000ff80")));
    CHECK(Near(Px(f.soft, 20, 20), 128, 0, 0, 1));    // red alone
    CHECK(Near(Px(f.soft, 60, 60), 0, 0, 128, 1));    // blue alone
    CHECK(Near(Px(f.soft, 40, 40), 64, 0, 128, 2));   // blue over red
    CHECK(Near(Px(f.soft, 80, 80), 0, 0, 0, 0));
    // Alpha 0 draws nothing; alpha ff replaces.
    f.Run(Doc(Rect(10, 10, 40, 40, "#ffffff00") + "," + Rect(60, 60, 20, 20, "#12ab34ff")));
    CHECK(Near(Px(f.soft, 20, 20), 0, 0, 0, 0) && Near(Px(f.soft, 70, 70), 0x12, 0xab, 0x34, 0));
}

TEST(raster_antialiasing, "raster.antialiasing") {
    Fixture f(100, 100);
    // A circle centred on a pixel corner: pixels whose centre is exactly on the edge get half coverage.
    f.Run(Doc("{\"t\":\"circle\",\"cx\":50.5,\"cy\":50.5,\"r\":20,\"fill\":\"#ffffff\"}"));
    const int edge = Px(f.soft, 50, 30).r;   // centre (50.5, 30.5): distance 20.0
    CHECK_MSG(edge > 100 && edge < 156, std::to_string(edge));
    CHECK(Px(f.soft, 50, 35).r == 255 && Px(f.soft, 50, 26).r == 0 && Px(f.soft, 50, 50).r == 255);
    const double area = Coverage(f.soft, 0, 0, 100, 100);
    CHECK_MSG(std::fabs(area - 3.14159265 * 400.0) < 18.0, std::to_string(area));
    // A rectangle on half-pixel edges: half coverage along each edge, none or full elsewhere.
    f.Run(Doc(Rect(10.5f, 10.5f, 10, 10)));
    CHECK(std::abs(Px(f.soft, 10, 15).r - 128) <= 10 && std::abs(Px(f.soft, 20, 15).r - 128) <= 10);
    CHECK(std::abs(Px(f.soft, 15, 10).r - 128) <= 10 && std::abs(Px(f.soft, 15, 20).r - 128) <= 10);
    CHECK(Px(f.soft, 15, 15).r == 255 && Px(f.soft, 9, 15).r == 0 && Px(f.soft, 21, 15).r == 0);
    // Coverage is conserved for shapes of any placement.
    f.Run(Doc(Rect(10.3f, 10.7f, 20.4f, 12.2f)));
    CHECK(std::fabs(Coverage(f.soft, 0, 0, 100, 100) - 20.4 * 12.2) < 1.2);
    // Rounded rectangle: area of a rect with quarter circles cut.
    f.Run(Doc(Rect(20, 20, 60, 40, "#ffffff", 10)));
    CHECK(std::fabs(Coverage(f.soft, 0, 0, 100, 100) - (60.0 * 40.0 - (4 - 3.14159265) * 100.0)) < 8.0);
    CHECK(Px(f.soft, 20, 20).r < 40 && Px(f.soft, 25, 25).r > 200);
}

TEST(raster_lines, "raster.lines-and-widths") {
    Fixture f(100, 100);
    const auto line = [&](float y, float width) {
        f.Run(Doc("{\"t\":\"line\",\"x1\":10,\"y1\":" + std::to_string(y) + ",\"x2\":90,\"y2\":" + std::to_string(y) + ",\"color\":\"#ffffff\",\"sw\":" + std::to_string(width) + "}"));
        return Coverage(f.soft, 50, 0, 51, 100);   // coverage across the line at x=50
    };
    CHECK(std::fabs(line(20.5f, 1) - 1.0) < 0.06);
    CHECK(std::fabs(line(20.0f, 1) - 1.0) < 0.06);
    CHECK(std::fabs(line(20.5f, 3) - 3.0) < 0.08);
    CHECK(std::fabs(line(33.3f, 6.5f) - 6.5) < 0.1);
    CHECK(std::fabs(line(20.5f, 0.5f) - 0.5) < 0.06);   // a hairline keeps its coverage
    CHECK(line(20.5f, 0) == 0.0);
    // Butt ends stop at the endpoints; the stroke does not extend past x=10 or x=90.
    line(20.5f, 4);
    CHECK(Px(f.soft, 9, 20).r == 0 && Px(f.soft, 10, 20).r == 255 && Px(f.soft, 89, 20).r == 255 && Px(f.soft, 90, 20).r == 0);
    // A diagonal line has the right width across.
    f.Run(Doc("{\"t\":\"line\",\"x1\":10,\"y1\":10,\"x2\":90,\"y2\":90,\"color\":\"#ffffff\",\"sw\":4}"));
    {
        const double diag = Coverage(f.soft, 0, 0, 100, 100);
        // Butt ends are not anti-aliased: pixel centres lying exactly on the cut of a 45 degree line decide a few pixels.
        CHECK_MSG(std::fabs(diag - 4.0 * 80.0 * 1.41421356) < 14.0, std::to_string(diag));
    }
}

TEST(raster_caps, "raster.caps-butt-and-round") {
    Fixture f(200, 200);
    const std::string arc = "{\"t\":\"arc\",\"cx\":100,\"cy\":100,\"r\":50,\"a0\":-30,\"a1\":30,\"color\":\"#ffffff\",\"sw\":10,\"cap\":\"";
    // The arc ends at angle 30: (125, 56.7); its tangent there is (cos30, sin30).
    const int px = 127, py = 58;   // about 3 px beyond the end along the tangent
    f.Run(Doc(arc + "butt\"}", 200, 200));
    CHECK(Px(f.soft, 100, 50).r == 255 && Px(f.soft, 123, 55).r > 200);
    CHECK_MSG(Px(f.soft, px, py).r < 30, std::to_string(Px(f.soft, px, py).r));
    CHECK(Px(f.soft, 150, 100).r == 0 && Px(f.soft, 100, 100).r == 0);
    f.Run(Doc(arc + "round\"}", 200, 200));
    CHECK_MSG(Px(f.soft, px, py).r > 200, std::to_string(Px(f.soft, px, py).r));
    CHECK(Px(f.soft, 100, 50).r == 255 && Px(f.soft, 150, 100).r == 0);
    // A full turn closes without caps or a seam.
    f.Run(Doc("{\"t\":\"arc\",\"cx\":100,\"cy\":100,\"r\":50,\"a0\":0,\"a1\":360,\"color\":\"#ffffff\",\"sw\":6,\"cap\":\"round\"}", 200, 200));
    CHECK(Px(f.soft, 100, 50).r == 255 && Px(f.soft, 150, 100).r == 255 && Px(f.soft, 100, 150).r == 255 && Px(f.soft, 100, 100).r == 0);
    CHECK(std::fabs(Coverage(f.soft, 0, 0, 200, 200) - 2 * 3.14159265 * 50 * 6) < 25.0);
    // Round cap on a segment: it extends half the width past the end.
    f.Run(Doc("{\"t\":\"line\",\"x1\":50,\"y1\":100,\"x2\":150,\"y2\":100,\"color\":\"#ffffff\",\"sw\":10,\"cap\":\"round\"}", 200, 200));
    CHECK(Px(f.soft, 46, 100).r > 200 && Px(f.soft, 153, 100).r > 200 && Px(f.soft, 43, 100).r == 0 && Px(f.soft, 157, 100).r == 0);
    CHECK(Px(f.soft, 46, 96).r < 200);   // the cap is round, not square
    // Angles: 0 is up and the sweep is clockwise.
    f.Run(Doc("{\"t\":\"arc\",\"cx\":100,\"cy\":100,\"r\":50,\"a0\":80,\"a1\":100,\"color\":\"#ffffff\",\"sw\":8}", 200, 200));
    CHECK(Px(f.soft, 150, 100).r > 200 && Px(f.soft, 100, 50).r == 0 && Px(f.soft, 100, 150).r == 0 && Px(f.soft, 50, 100).r == 0);
    f.Run(Doc("{\"t\":\"arc\",\"cx\":100,\"cy\":100,\"r\":50,\"a0\":170,\"a1\":190,\"color\":\"#ffffff\",\"sw\":8}", 200, 200));
    CHECK(Px(f.soft, 100, 149).r > 200 && Px(f.soft, 100, 51).r == 0);
}

TEST(raster_polygons, "raster.polygons") {
    Fixture f(100, 100);
    // An L shape (concave) in both windings.
    const char* l1 = "[10,10,60,10,60,30,30,30,30,60,10,60]";
    const char* l2 = "[10,60,30,60,30,30,60,30,60,10,10,10]";
    for (const char* pts : {l1, l2}) {
        f.Run(Doc(std::string("{\"t\":\"poly\",\"pts\":") + pts + ",\"fill\":\"#ffffff\"}"));
        CHECK(Px(f.soft, 20, 20).r == 255 && Px(f.soft, 50, 20).r == 255 && Px(f.soft, 20, 50).r == 255);
        CHECK(Px(f.soft, 45, 45).r == 0 && Px(f.soft, 65, 20).r == 0 && Px(f.soft, 5, 5).r == 0);
        CHECK(std::fabs(Coverage(f.soft, 0, 0, 100, 100) - (50.0 * 20.0 + 20.0 * 30.0)) < 4.0);
    }
    // Open polyline: only a stroke; the segment between the last and first point is not drawn.
    f.Run(Doc("{\"t\":\"poly\",\"pts\":[20,20,80,20,80,80],\"stroke\":\"#ffffff\",\"sw\":4,\"closed\":false}"));
    CHECK(Px(f.soft, 50, 20).r > 200 && Px(f.soft, 80, 50).r > 200 && Px(f.soft, 50, 50).r == 0);
    CHECK(Px(f.soft, 40, 52).r == 0);   // where the closing diagonal would run
    f.Run(Doc("{\"t\":\"poly\",\"pts\":[20,20,80,20,80,80],\"stroke\":\"#ffffff\",\"sw\":4,\"closed\":true}"));
    CHECK(Px(f.soft, 50, 50).r > 200);  // the diagonal 20,20 -> 80,80 passes through (50,50)
    // Fill and stroke together: the stroke is centred on the edge.
    f.Run(Doc("{\"t\":\"rect\",\"x\":20,\"y\":20,\"w\":60,\"h\":60,\"fill\":\"#0000ff\",\"stroke\":\"#ff0000\",\"sw\":8}"));
    CHECK(Near(Px(f.soft, 50, 50), 0, 0, 255, 0) && Near(Px(f.soft, 21, 50), 255, 0, 0, 0) && Near(Px(f.soft, 17, 50), 255, 0, 0, 0));
    CHECK(Near(Px(f.soft, 15, 50), 0, 0, 0, 0) && Near(Px(f.soft, 25, 50), 0, 0, 255, 0));
    // Degenerate shapes draw nothing and do not fail.
    f.Run(Doc("{\"t\":\"poly\",\"pts\":[10,10,10,10,10,10],\"fill\":\"#ffffff\"},{\"t\":\"rect\",\"x\":5,\"y\":5,\"w\":0,\"h\":10,\"fill\":\"#ffffff\"},"
                "{\"t\":\"circle\",\"cx\":5,\"cy\":5,\"r\":0,\"fill\":\"#ffffff\"},{\"t\":\"poly\",\"pts\":[1,1,50,50,99,99],\"fill\":\"#ffffff\"}"));
    CHECK(Coverage(f.soft, 0, 0, 100, 100) < 1.0);
}

TEST(raster_clip, "raster.clipping") {
    Fixture f(100, 100);
    f.Run(Doc("{\"t\":\"clip\",\"kind\":\"rect\",\"x\":20,\"y\":20,\"w\":30,\"h\":30}," + Rect(0, 0, 100, 100) + ",{\"t\":\"unclip\"}," + Rect(70, 70, 10, 10)));
    CHECK(Px(f.soft, 30, 30).r == 255 && Px(f.soft, 49, 49).r == 255);
    CHECK(Px(f.soft, 19, 30).r == 0 && Px(f.soft, 50, 30).r == 0 && Px(f.soft, 10, 10).r == 0 && Px(f.soft, 60, 60).r == 0);
    CHECK(Px(f.soft, 75, 75).r == 255);   // after unclip, drawing is unrestricted
    // A circle clip, nested with a rectangle clip: only the intersection shows.
    f.Run(Doc("{\"t\":\"clip\",\"kind\":\"circle\",\"cx\":50,\"cy\":50,\"r\":20},{\"t\":\"clip\",\"kind\":\"rect\",\"x\":50,\"y\":0,\"w\":100,\"h\":100}," + Rect(0, 0, 100, 100)));
    CHECK(Px(f.soft, 60, 50).r == 255 && Px(f.soft, 40, 50).r == 0 && Px(f.soft, 80, 50).r == 0 && Px(f.soft, 50, 69).r == 255 && Px(f.soft, 50, 75).r == 0);
    CHECK(Px(f.soft, 68, 62).r == 0 || Px(f.soft, 68, 62).r == 255);   // the corner of the circle: inside or out, never garbage
    CHECK(std::fabs(Coverage(f.soft, 0, 0, 100, 100) - 3.14159265 * 400 / 2) < 14.0);
    // Clipped translucent geometry keeps its blend; a clip reaching outside the view is harmless.
    f.Run(Doc("{\"t\":\"clip\",\"kind\":\"rect\",\"x\":-500,\"y\":-500,\"w\":2000,\"h\":2000}," + Rect(10, 10, 20, 20, "#ffffff80")));
    CHECK(Near(Px(f.soft, 20, 20), 128, 128, 128, 1));
    // An empty clip hides everything; a rotated clip follows the transform.
    f.Run(Doc("{\"t\":\"clip\",\"kind\":\"rect\",\"x\":10,\"y\":10,\"w\":0,\"h\":50}," + Rect(0, 0, 100, 100)));
    CHECK(Coverage(f.soft, 0, 0, 100, 100) == 0.0);
    f.Run(Doc("{\"t\":\"push\",\"tx\":50,\"ty\":50,\"rot\":45},{\"t\":\"clip\",\"kind\":\"rect\",\"x\":-10,\"y\":-10,\"w\":20,\"h\":20},{\"t\":\"pop\"}," + Rect(0, 0, 100, 100)));
    CHECK(Px(f.soft, 50, 50).r == 255 && Px(f.soft, 50, 38).r == 255 && Px(f.soft, 61, 50).r == 255);   // diamond reaching ~14 px
    CHECK(Px(f.soft, 60, 40).r == 0 && Px(f.soft, 40, 40).r == 0);                                       // the unrotated square's corners are gone
}

TEST(raster_transform, "raster.transforms") {
    Fixture f(200, 200);
    // translate, rotate 90 clockwise, scale 2: local (0..10, 0..5) lands on x 90..100, y 50..70.
    f.Run(Doc("{\"t\":\"push\",\"tx\":100,\"ty\":50,\"rot\":90,\"sx\":2,\"sy\":2}," + Rect(0, 0, 10, 5) + ",{\"t\":\"pop\"}," + Rect(150, 150, 10, 10), 200, 200));
    CHECK(Px(f.soft, 95, 60).r == 255 && Px(f.soft, 91, 52).r == 255 && Px(f.soft, 98, 68).r == 255);
    CHECK(Px(f.soft, 105, 60).r == 0 && Px(f.soft, 95, 45).r == 0 && Px(f.soft, 95, 75).r == 0 && Px(f.soft, 85, 60).r == 0);
    CHECK(Px(f.soft, 155, 155).r == 255);   // popped
    // Nested transforms compose (outer first).
    f.Run(Doc("{\"t\":\"push\",\"tx\":50,\"ty\":50},{\"t\":\"push\",\"sx\":3,\"sy\":3}," + Rect(10, 10, 5, 5) + ",{\"t\":\"pop\"},{\"t\":\"pop\"}", 200, 200));
    CHECK(Px(f.soft, 85, 85).r == 255 && Px(f.soft, 70, 70).r == 0 && Px(f.soft, 99, 99).r == 0);
    // The stroke width follows the scale.
    f.Run(Doc("{\"t\":\"push\",\"sx\":4,\"sy\":4},{\"t\":\"line\",\"x1\":5,\"y1\":10,\"x2\":40,\"y2\":10,\"color\":\"#ffffff\",\"sw\":2}", 200, 200));
    CHECK(std::fabs(Coverage(f.soft, 100, 0, 101, 200) - 8.0) < 0.2);
    // Unbalanced pop and a scale of zero are harmless.
    f.Run(Doc("{\"t\":\"pop\"},{\"t\":\"push\",\"sx\":0,\"sy\":0}," + Rect(0, 0, 50, 50), 200, 200));
    CHECK(Coverage(f.soft, 0, 0, 200, 200) < 1.0);
}

TEST(raster_view_scaling, "raster.canvas-scaled-to-view") {
    // The canvas is scaled by min(width/w, height/h) and centred.
    {
        Fixture f(960, 1080);
        f.Run(Doc(Rect(0, 0, 1920, 1080), 1920, 1080), 0xFF202020u);
        CHECK(Near(Px(f.soft, 480, 540), 255, 255, 255, 0) && Near(Px(f.soft, 0, 270), 255, 255, 255, 0) && Near(Px(f.soft, 959, 809), 255, 255, 255, 0));
        CHECK(Near(Px(f.soft, 480, 100), 0x20, 0x20, 0x20, 0) && Near(Px(f.soft, 480, 900), 0x20, 0x20, 0x20, 0));
    }
    {
        Fixture f(1920, 540);
        f.Run(Doc(Rect(0, 0, 1920, 1080), 1920, 1080), 0xFF202020u);
        CHECK(Near(Px(f.soft, 960, 270), 255, 255, 255, 0) && Near(Px(f.soft, 480, 0), 255, 255, 255, 0) && Near(Px(f.soft, 1439, 539), 255, 255, 255, 0));
        CHECK(Near(Px(f.soft, 100, 270), 0x20, 0x20, 0x20, 0) && Near(Px(f.soft, 1500, 270), 0x20, 0x20, 0x20, 0));
    }
    {   // A canvas larger than the view scales text and strokes with it.
        Fixture f(480, 270);
        f.Run(Doc("{\"t\":\"line\",\"x1\":0,\"y1\":540,\"x2\":1920,\"y2\":540,\"color\":\"#ffffff\",\"sw\":8}", 1920, 1080));
        CHECK(std::fabs(Coverage(f.soft, 240, 0, 241, 270) - 2.0) < 0.1);
    }
}

TEST(raster_images, "raster.images") {
    Fixture f(100, 100);
    const std::vector<uint8_t> white = {255, 255, 255, 255};
    const std::vector<uint8_t> red = {255, 0, 0, 255};
    CHECK(f.renderer.PutImage("w", 1, 1, std::vector<uint8_t>(white)) == ImageResult::Ok);
    CHECK(f.renderer.PutImage("r", 1, 1, std::vector<uint8_t>(red)) == ImageResult::Ok);
    const auto image = [](const char* id, const char* extra = "") {
        return std::string("{\"t\":\"image\",\"id\":\"") + id + "\",\"x\":10,\"y\":10,\"w\":20,\"h\":20" + extra + "}";
    };
    f.Run(Doc(image("r")));
    CHECK(Near(Px(f.soft, 20, 20), 255, 0, 0, 0) && Near(Px(f.soft, 9, 20), 0, 0, 0, 0) && Near(Px(f.soft, 30, 20), 0, 0, 0, 0));
    f.Run(Doc(image("r", ",\"op\":0.5")));
    CHECK(Near(Px(f.soft, 20, 20), 128, 0, 0, 1));
    f.Run(Doc(image("w", ",\"tint\":\"#ff8000\"")));
    CHECK(Near(Px(f.soft, 20, 20), 255, 128, 0, 1));
    f.Run(Doc(image("r", ",\"tint\":\"#00ff00\"")));   // red times green is black
    CHECK(Near(Px(f.soft, 20, 20), 0, 0, 0, 0));
    f.Run(Doc(image("w", ",\"tint\":\"#ffffff80\",\"op\":0.5")));   // tint alpha and opacity multiply
    CHECK(Near(Px(f.soft, 20, 20), 64, 64, 64, 2));
    // Rotation about the centre: a 20x10 image turned 90 degrees covers 10x20.
    f.Run(Doc("{\"t\":\"image\",\"id\":\"w\",\"x\":40,\"y\":45,\"w\":20,\"h\":10,\"rot\":90}"));
    CHECK(Px(f.soft, 50, 42).r == 255 && Px(f.soft, 50, 57).r == 255 && Px(f.soft, 42, 50).r == 0 && Px(f.soft, 58, 50).r == 0 && Px(f.soft, 50, 38).r == 0 && Px(f.soft, 50, 62).r == 0);
    // A missing id draws nothing and is counted; dropping an image removes it.
    f.Run(Doc(image("nope")));
    CHECK(Coverage(f.soft, 0, 0, 100, 100) == 0.0 && f.renderer.Stats().missingImages >= 1);
    CHECK(f.renderer.DropImage("r") && !f.renderer.DropImage("r"));
    f.Run(Doc(image("r")));
    CHECK(Coverage(f.soft, 0, 0, 100, 100) == 0.0);
    // Replacing an image takes effect on the next frame.
    CHECK(f.renderer.PutImage("w", 1, 1, std::vector<uint8_t>(red)) == ImageResult::Ok);
    f.Run(Doc(image("w")));
    CHECK(Near(Px(f.soft, 20, 20), 255, 0, 0, 0));
    // Transparent pixels stay transparent (straight alpha).
    CHECK(f.renderer.PutImage("t", 2, 1, std::vector<uint8_t>{255, 255, 255, 255, 255, 255, 255, 0}) == ImageResult::Ok);
    f.Run(Doc("{\"t\":\"image\",\"id\":\"t\",\"x\":0,\"y\":0,\"w\":40,\"h\":20}"));
    CHECK(Px(f.soft, 5, 10).r == 255 && Px(f.soft, 35, 10).r == 0);
}

TEST(raster_image_limits, "raster.image-limits") {
    FontRegistry fonts;
    GdiGlyphSource glyphs(fonts);
    Renderer r(glyphs);
    CHECK(r.PutImage("", 1, 1, std::vector<uint8_t>(4)) == ImageResult::Invalid);
    CHECK(r.PutImage("a", 0, 1, std::vector<uint8_t>()) == ImageResult::Invalid);
    CHECK(r.PutImage("a", 2, 2, std::vector<uint8_t>(15)) == ImageResult::Invalid);        // wrong byte count
    CHECK(r.PutImage(std::string(65, 'x'), 1, 1, std::vector<uint8_t>(4)) == ImageResult::Invalid);
    CHECK(r.PutImage("big", 1025, 1024, std::vector<uint8_t>(1025u * 1024 * 4)) == ImageResult::TooBig);   // over 4 MiB
    CHECK(r.PutImage("max", 1024, 1024, std::vector<uint8_t>(4u << 20)) == ImageResult::Ok);               // exactly 4 MiB
    CHECK(r.DropImage("max"));
    for (int i = 0; i < 128; ++i) CHECK(r.PutImage("i" + std::to_string(i), 1, 1, std::vector<uint8_t>(4)) == ImageResult::Ok);
    CHECK(r.PutImage("i128", 1, 1, std::vector<uint8_t>(4)) == ImageResult::TooMany);
    CHECK(r.PutImage("i0", 2, 1, std::vector<uint8_t>(8)) == ImageResult::Ok);   // replacing is not a new image
    CHECK(r.ImageCount() == 128);
    // The total budget protects the 32-bit host.
    Renderer budget(glyphs);
    int accepted = 0;
    for (int i = 0; i < 12; ++i) {
        if (budget.PutImage("b" + std::to_string(i), 1024, 1024, std::vector<uint8_t>(4u << 20)) == ImageResult::Ok) ++accepted;
    }
    CHECK(accepted == 8);
}

TEST(raster_text, "raster.text") {
    Fixture f(400, 200);
    if (!RegisterTestFonts(f.fonts)) SKIP("no Arial or Segoe UI in the Windows fonts folder");
    // Coverage is non-empty and sits where the text was asked to be.
    f.Run(Doc(Text("Hello", 20, 30, 32), 400, 200));
    const Box hello = Changed(f.soft, 0, 0, 400, 200, {0, 0, 0, 255}, 40);
    CHECK_MSG(hello.count > 200, std::to_string(hello.count));
    CHECK(hello.x0 >= 18 && hello.x0 <= 26 && hello.x1 > 80 && hello.x1 < 140 && hello.y0 >= 30 && hello.y1 < 70);
    // Size scales the text.
    f.Run(Doc(Text("Hello", 20, 30, 64), 400, 200));
    const Box big = Changed(f.soft, 0, 0, 400, 200, {0, 0, 0, 255}, 40);
    CHECK((big.x1 - big.x0) > (hello.x1 - hello.x0) * 3 / 2);
    // Alignment.
    f.Run(Doc(Text("Hello", 200, 30, 32, ",\"align\":\"c\""), 400, 200));
    Box b = Changed(f.soft, 0, 0, 400, 200, {0, 0, 0, 255}, 40);
    CHECK_MSG(std::abs((b.x0 + b.x1) / 2 - 200) <= 4, std::to_string((b.x0 + b.x1) / 2));
    f.Run(Doc(Text("Hello", 200, 30, 32, ",\"align\":\"r\""), 400, 200));
    b = Changed(f.soft, 0, 0, 400, 200, {0, 0, 0, 255}, 40);
    CHECK_MSG(std::abs(b.x1 - 200) <= 4, std::to_string(b.x1));
    // Baselines: "H" has no descender, so its bottom is the baseline.
    f.Run(Doc(Text("H", 50, 100, 40, ",\"base\":\"alpha\""), 400, 200));
    b = Changed(f.soft, 0, 0, 400, 200, {0, 0, 0, 255}, 60);
    CHECK_MSG(std::abs(b.y1 - 99) <= 1, std::to_string(b.y1));
    f.Run(Doc(Text("H", 50, 100, 40, ",\"base\":\"mid\""), 400, 200));
    b = Changed(f.soft, 0, 0, 400, 200, {0, 0, 0, 255}, 60);
    CHECK_MSG(std::abs((b.y0 + b.y1) / 2 - 100) <= 8, std::to_string(b.y0) + ".." + std::to_string(b.y1));
    f.Run(Doc(Text("H", 50, 100, 40, ",\"base\":\"top\""), 400, 200));
    b = Changed(f.soft, 0, 0, 400, 200, {0, 0, 0, 255}, 60);
    CHECK_MSG(b.y0 >= 100 && b.y0 <= 112, std::to_string(b.y0));
    // maxw cuts the text with "..." inside the width.
    f.Run(Doc(Text("A very long caption that does not fit", 10, 30, 24, ",\"maxw\":150"), 400, 200));
    b = Changed(f.soft, 0, 0, 400, 200, {0, 0, 0, 255}, 40);
    CHECK_MSG(b.count > 100 && b.x1 <= 10 + 150 + 2, std::to_string(b.x1));
    const Box tail = Changed(f.soft, 10 + 150 - 14, 0, 160, 200, {0, 0, 0, 255}, 40);
    CHECK(tail.count > 3);   // the dots
    f.Run(Doc(Text("Fits", 10, 30, 24, ",\"maxw\":150"), 400, 200));
    const Box fits = Changed(f.soft, 0, 0, 400, 200, {0, 0, 0, 255}, 40);
    f.Run(Doc(Text("Fits", 10, 30, 24), 400, 200));
    const Box plain = Changed(f.soft, 0, 0, 400, 200, {0, 0, 0, 255}, 40);
    CHECK(fits.count == plain.count && fits.x1 == plain.x1);
    f.Run(Doc(Text("anything", 10, 30, 24, ",\"maxw\":2"), 400, 200));
    CHECK(Changed(f.soft, 0, 0, 400, 200, {0, 0, 0, 255}, 40).Empty());
    // Outline: a halo of the outline colour around the glyphs.
    f.Run(Doc(Text("O", 50, 30, 60, ",\"outline\":\"#ff0000ff\""), 400, 200));
    int red = 0;
    for (int y = 0; y < 200; ++y) {
        for (int x = 0; x < 400; ++x) {
            const Rgba c = Px(f.soft, x, y);
            if (c.r > 200 && c.g < 60 && c.b < 60) ++red;
        }
    }
    CHECK_MSG(red > 80, std::to_string(red));
    // Colour and alpha apply to the glyph coverage.
    f.Run(Doc("{\"t\":\"text\",\"x\":20,\"y\":20,\"s\":\"MMMM\",\"size\":50,\"color\":\"#00ff0080\"}", 400, 200));
    int green = 0, bright = 0;
    for (int y = 0; y < 200; ++y) {
        for (int x = 0; x < 400; ++x) {
            const Rgba c = Px(f.soft, x, y);
            if (c.g > 0) ++green;
            if (c.g > 140) ++bright;
            CHECK(c.r == 0 && c.b == 0 && c.g <= 129);
        }
    }
    CHECK(green > 400 && bright == 0);
}

TEST(raster_text_fallback, "raster.text-fallbacks") {
    // No font registered: the system UI font draws every role.
    Fixture f(400, 100);
    f.Run(Doc(Text("Plain", 10, 10, 30, ",\"font\":\"black\""), 400, 100));
    CHECK(Changed(f.soft, 0, 0, 400, 100, {0, 0, 0, 255}, 40).count > 150);
    CHECK(f.renderer.Stats().glyphFailures == 0);
    // A character the registered font lacks falls back to the system font, never to a question mark or nothing.
    if (!RegisterTestFonts(f.fonts)) SKIP("no Arial or Segoe UI in the Windows fonts folder");
    f.Run(Doc(Text("\\u6f22\\u5b57", 10, 10, 40), 400, 100));
    const Box han = Changed(f.soft, 0, 0, 400, 100, {0, 0, 0, 255}, 40);
    CHECK_MSG(han.count > 100, std::to_string(han.count));
    f.Run(Doc(Text("?", 10, 10, 40), 400, 100));
    const int question = Changed(f.soft, 0, 0, 400, 100, {0, 0, 0, 255}, 40).count;
    CHECK(question > 20 && question != han.count);
    // Characters beyond the BMP and control characters do not crash and still advance.
    f.Run(Doc(Text("a\\ud83d\\ude00b\\u0001c", 10, 10, 30), 400, 100));
    CHECK(Changed(f.soft, 0, 0, 400, 100, {0, 0, 0, 255}, 40).count > 50);
    // A role without a file uses the regular file, not the system font: bold is registered, medium is not.
    f.Run(Doc(Text("Role", 10, 10, 30, ",\"font\":\"medium\""), 400, 100));
    const int medium = Changed(f.soft, 0, 0, 400, 100, {0, 0, 0, 255}, 40).count;
    f.Run(Doc(Text("Role", 10, 10, 30, ",\"font\":\"regular\""), 400, 100));
    CHECK(medium == Changed(f.soft, 0, 0, 400, 100, {0, 0, 0, 255}, 40).count);
}

TEST(raster_fonts, "raster.font-registration") {
    FontRegistry fonts;
    std::string error;
    const std::wstring arial = WindowsFont(L"arial.ttf");
    const std::wstring bold = WindowsFont(L"arialbd.ttf");
    if (arial.empty() || bold.empty()) SKIP("no Arial in the Windows fonts folder");
    // The sfnt reader finds the family and the weight.
    FontFace face;
    const auto reader = [](FILE* file) {
        return [file](uint64_t offset, void* dst, size_t length) {
            return _fseeki64(file, static_cast<long long>(offset), SEEK_SET) == 0 && fread(dst, 1, length, file) == length;
        };
    };
    FILE* file = nullptr;
    CHECK(_wfopen_s(&file, arial.c_str(), L"rb") == 0 && file);
    _fseeki64(file, 0, SEEK_END);
    const long long size = _ftelli64(file);
    CHECK(ReadSfntInfo(reader(file), static_cast<uint64_t>(size), face, error));
    CHECK_MSG(face.family == L"Arial" && face.weight == 400 && !face.italic, error);
    // Hostile or truncated input is refused, never trusted.
    CHECK(!ReadSfntInfo(reader(file), 8, face, error));
    CHECK(!ReadSfntInfo(reader(file), 0, face, error));
    CHECK(!ReadSfntInfo([](uint64_t, void*, size_t) { return false; }, 1000, face, error));
    fclose(file);
    std::vector<uint8_t> noise(4096);
    uint32_t seed = 12345;
    for (int round = 0; round < 3000; ++round) {
        for (uint8_t& b : noise) {
            seed = seed * 1664525u + 1013904223u;
            b = static_cast<uint8_t>(seed >> 24);
        }
        if (round % 3 == 0) {   // plausible header, hostile body
            noise[0] = 0; noise[1] = 1; noise[2] = 0; noise[3] = 0; noise[4] = 0; noise[5] = static_cast<uint8_t>(1 + round % 40);
        }
        ReadSfntInfo([&](uint64_t offset, void* dst, size_t length) {
            if (offset + length > noise.size()) return false;
            std::memcpy(dst, noise.data() + offset, length);
            return true;
        }, noise.size(), face, error);
    }
    // Registering.
    CHECK(fonts.Put(FontRole::Regular, arial, error));
    CHECK(fonts.Put(FontRole::Bold, bold, error));
    CHECK(fonts.Count() == 2);
    FontFace got;
    CHECK(fonts.Get(FontRole::Bold, got) && got.weight == 700 && !fonts.Get(FontRole::Black, got));
    const uint32_t generation = fonts.Generation();
    CHECK(fonts.Put(FontRole::Regular, arial, error) && fonts.Generation() != generation && fonts.Count() == 2);
    // Refusals.
    CHECK(!fonts.Put(FontRole::Black, L"C:\\no\\such\\font.ttf", error) && !error.empty());
    CHECK(!fonts.Put(FontRole::Black, L"C:\\Windows", error));   // a directory
    wchar_t temp[MAX_PATH];
    GetTempPathW(MAX_PATH, temp);
    const std::wstring junk = std::wstring(temp) + L"ohud-junk-" + std::to_wstring(GetCurrentProcessId()) + L".ttf";
    {
        std::ofstream out(junk, std::ios::binary);
        out << "this is not a font at all, just text that is long enough to be read";
    }
    CHECK(!fonts.Put(FontRole::Black, junk, error));
    {   // a valid header cut short
        std::ifstream in(arial, std::ios::binary);
        std::vector<char> head(200);
        in.read(head.data(), 200);
        std::ofstream out(junk, std::ios::binary);
        out.write(head.data(), 200);
    }
    CHECK(!fonts.Put(FontRole::Black, junk, error));
    DeleteFileW(junk.c_str());
    CHECK(fonts.Count() == 2);
    fonts.Clear();
    CHECK(fonts.Count() == 0);
}

TEST(raster_atlas_eviction, "raster.glyph-atlas-eviction") {
    Fixture f(300, 100);
    RegisterTestFonts(f.fonts);
    // Reference rendering of a word in a fresh renderer.
    f.Run(Doc(Text("Evict", 10, 10, 24), 300, 100));
    const Box before = Changed(f.soft, 0, 0, 300, 100, {0, 0, 0, 255}, 40);
    std::vector<uint8_t> reference = f.soft.Pixels();
    CHECK(before.count > 100);
    // Draw far more distinct glyphs than the 1024 small cells hold.
    for (int page = 0; page < 3; ++page) {
        std::string many;
        for (int i = 0; i < 700; ++i) {
            const unsigned cp = 0x4E00u + static_cast<unsigned>(page * 700 + i);
            char u[16];
            sprintf_s(u, "\\u%04x", cp);
            many += u;
        }
        f.Run(Doc(Text(many, 0, 0, 20), 300, 100));
    }
    CHECK_MSG(f.renderer.Stats().atlasEvictions > 500, std::to_string(f.renderer.Stats().atlasEvictions));
    CHECK(f.renderer.GlyphsResident() <= 1024);
    CHECK(f.renderer.Stats().glyphFailures == 0);
    // After the churn the original word renders identically.
    f.Run(Doc(Text("Evict", 10, 10, 24), 300, 100));
    CHECK(f.soft.Pixels() == reference);
    // Glyph sizes beyond the small cell use the large atlas; sizes beyond it are skipped without failing.
    f.Run(Doc(Text("W", 10, 0, 100), 300, 100));
    CHECK(Changed(f.soft, 0, 0, 300, 100, {0, 0, 0, 255}, 40).count > 500);
    const uint64_t failuresBefore = f.renderer.Stats().glyphFailures;
    f.Run(Doc(Text("W", 10, 0, 500), 300, 100));
    CHECK(f.renderer.Stats().glyphFailures >= failuresBefore);
}

TEST(raster_deterministic, "raster.deterministic-and-batching") {
    Fixture a(480, 270), b(480, 270);
    RegisterTestFonts(a.fonts);
    RegisterTestFonts(b.fonts);
    std::string many;
    for (int i = 0; i < 2000; ++i) {
        many += std::string(i ? "," : "") + "{\"t\":\"circle\",\"cx\":" + std::to_string(10 + (i * 37) % 1900) + ",\"cy\":" + std::to_string(10 + (i * 53) % 1060) +
                ",\"r\":" + std::to_string(5 + i % 17) + ",\"fill\":\"#2060c0" + (i % 2 ? "80" : "ff") + "\",\"stroke\":\"#ffffff\",\"sw\":1.5}";
    }
    many += "," + Text("batching", 100, 100, 40);
    const std::string scene = Doc(many, 1920, 1080);
    a.Run(scene, 0xFF101010u);
    b.Run(scene, 0xFF101010u);
    CHECK(a.soft.Pixels() == b.soft.Pixels());
    const RenderStats s = a.renderer.Stats();
    CHECK_MSG(s.drawCalls >= 3, "2000 shaped circles exceed one batch (draw calls: " + std::to_string(s.drawCalls) + ")");
    CHECK(s.commandsDrawn == 2001);
    // The maximum of 8192 commands renders.
    std::string max;
    for (int i = 0; i < 8192; ++i) max += std::string(i ? "," : "") + "{\"t\":\"rect\",\"x\":" + std::to_string(i % 1900) + ",\"y\":" + std::to_string((i / 8) % 1000) + ",\"w\":20,\"h\":10,\"fill\":\"#ffffff10\",\"r\":3}";
    const auto start = std::chrono::steady_clock::now();
    a.Run(Doc(max, 1920, 1080));
    const auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - start).count();
    CHECK(a.renderer.Stats().commandsDrawn >= 8192 + 2001);
    CHECK_MSG(ms < 20000, "software reference rendering of 8192 commands took " + std::to_string(ms) + " ms");
}

TEST(raster_nothing, "raster.empty-scene-and-no-scene") {
    Fixture f(50, 50);
    f.soft.Clear(0xFF123456u);
    CHECK(f.Draw() == RenderResult::Nothing);   // no scene yet
    f.Load("{}");
    CHECK(f.Draw() == RenderResult::Nothing);
    f.Load("{\"v\":1,\"cmds\":[]}");
    CHECK(f.Draw() == RenderResult::Nothing);
    CHECK(Near(Px(f.soft, 10, 10), 0x12, 0x34, 0x56, 0));
    // A newer scene replaces the older one; the older one is never drawn after it.
    f.Load(Doc(Rect(0, 0, 50, 50, "#ff0000"), 50, 50));
    f.Load(Doc(Rect(0, 0, 50, 50, "#00ff00"), 50, 50));
    CHECK(f.Draw() == RenderResult::Drawn && Near(Px(f.soft, 10, 10), 0, 255, 0, 0));
}
