#include "Harness.h"

#include "Scene.h"

#include <string>

using namespace hud;

namespace {

std::string Vector(const char* name) {
    bool ok = false;
    const std::string text = t::ReadFile(t::GetConfig().vectorsDirectory + "/" + name, ok);
    if (!ok) SKIP("the golden vectors directory was not given (--vectors)");
    return text;
}

bool Parse(const std::string& text, Scene& scene, SceneError& error) { return ParseScene(text, scene, error); }

std::string Cmds(const std::string& commands) { return "{\"v\":1,\"w\":1920,\"h\":1080,\"cmds\":[" + commands + "]}"; }

} // namespace

TEST(scene_vector_minimal, "scene.vector-minimal") {
    Scene s;
    SceneError e;
    CHECK(Parse(Vector("scene-minimal.json"), s, e));
    CHECK(s.w == 1920 && s.h == 1080 && !s.background && s.cmds.size() == 2);
    CHECK(s.cmds[0].kind == CmdKind::Rect && s.cmds[0].r == 12 && s.cmds[0].hasFill && s.cmds[0].fill.a == 0xd9 && !s.cmds[0].hasStroke);
    CHECK(s.cmds[1].kind == CmdKind::Text && s.cmds[1].text == "openHUD" && s.cmds[1].font == FontRole::Bold && s.cmds[1].base == Base::Mid && s.cmds[1].size == 22);
    CHECK(s.unknownCommands == 0 && s.skippedCommands == 0);
}

TEST(scene_vector_pause, "scene.vector-pause") {
    Scene s;
    SceneError e;
    CHECK(Parse(Vector("scene-pause.json"), s, e));
    CHECK(s.background && s.cmds.size() == 3);
    CHECK(s.cmds[2].kind == CmdKind::Text && s.cmds[2].align == Align::Center && s.cmds[2].base == Base::Mid);
}

TEST(scene_vector_radar, "scene.vector-radar") {
    Scene s;
    SceneError e;
    CHECK(Parse(Vector("scene-radar.json"), s, e));
    CHECK(s.cmds.size() == 8);
    CHECK(s.cmds[1].kind == CmdKind::Clip && s.cmds[1].clipCircle && s.cmds[1].r == 156);
    CHECK(s.cmds[2].kind == CmdKind::Push && s.cmds[2].rot == -35 && s.cmds[2].tx == 1700);
    CHECK(s.cmds[3].kind == CmdKind::Poly && s.cmds[3].pts.size() == 8 && !s.cmds[3].closed && s.cmds[3].hasStroke && !s.cmds[3].hasFill);
    CHECK(s.cmds[6].kind == CmdKind::Arc && s.cmds[6].cap == Cap::Round && s.cmds[6].a0 == -40);
    CHECK(s.cmds[7].kind == CmdKind::Image && s.cmds[7].text == "player" && s.cmds[7].op == 1 && !s.cmds[7].hasTint);
}

TEST(scene_empty, "scene.empty-documents") {
    Scene s;
    SceneError e;
    CHECK(Parse("{}", s, e) && s.cmds.empty());
    CHECK(Parse("{\"v\":1,\"cmds\":[]}", s, e) && s.cmds.empty() && s.w == 1920 && s.h == 1080);
    CHECK(Parse("{\"v\":1,\"w\":800,\"h\":600,\"background\":true,\"cmds\":[]}", s, e) && s.background && s.w == 800);
}

TEST(scene_refusals, "scene.refusals") {
    Scene s;
    SceneError e;
    CHECK(!Parse("not json", s, e) && e.code == "json_syntax");
    CHECK(!Parse("[]", s, e) && e.code == "bad_scene");
    CHECK(!Parse("\"x\"", s, e) && e.code == "bad_scene");
    CHECK(!Parse("{\"v\":2,\"cmds\":[]}", s, e) && e.code == "unsupported_version");
    CHECK(!Parse("{\"cmds\":[]}", s, e) && e.code == "unsupported_version");
    CHECK(!Parse("{\"v\":\"1\",\"cmds\":[]}", s, e) && e.code == "unsupported_version");
    CHECK(!Parse("{\"v\":1,\"cmds\":{}}", s, e) && e.code == "bad_scene");
    CHECK(!Parse("{\"v\":1,\"w\":0,\"h\":10,\"cmds\":[]}", s, e) && e.code == "bad_scene");
    CHECK(!Parse("{\"v\":1,\"w\":99999,\"h\":10,\"cmds\":[]}", s, e) && e.code == "bad_scene");
    CHECK(!Parse("{\"v\":1,\"w\":\"a\",\"h\":10,\"cmds\":[]}", s, e) && e.code == "bad_scene");
    CHECK(!Parse("{\"v\":1,\"background\":1,\"cmds\":[]}", s, e) && e.code == "bad_scene");
    // NaN and Infinity are not JSON: the scene is refused, never silently drawn.
    CHECK(!Parse(Cmds("{\"t\":\"rect\",\"x\":NaN,\"y\":0,\"w\":1,\"h\":1}"), s, e) && e.code == "json_syntax");
    CHECK(!Parse(Cmds("{\"t\":\"rect\",\"x\":Infinity,\"y\":0,\"w\":1,\"h\":1}"), s, e) && e.code == "json_syntax");
    CHECK(!Parse(Cmds("{\"t\":\"rect\",\"x\":1e999,\"y\":0,\"w\":1,\"h\":1}"), s, e) && e.code == "json_syntax");
}

TEST(scene_limits, "scene.limits") {
    Scene s;
    SceneError e;
    // 8192 commands pass, 8193 are refused.
    std::string ok, over;
    for (int i = 0; i < 8192; ++i) ok += std::string(i ? "," : "") + "{\"t\":\"pop\"}";
    over = ok + ",{\"t\":\"pop\"}";
    CHECK(Parse(Cmds(ok), s, e));
    CHECK(!Parse(Cmds(over), s, e) && e.code == "too_many_commands");
    // 1 MiB of JSON.
    std::string pad = "{\"v\":1,\"cmds\":[" + std::string(1u << 20, ' ') + "]}";
    CHECK(!Parse(pad, s, e) && e.code == "too_large");
    // Clip depth 8 passes, 9 is refused.
    std::string clips;
    for (int i = 0; i < 8; ++i) clips += std::string(i ? "," : "") + "{\"t\":\"clip\",\"kind\":\"rect\",\"x\":0,\"y\":0,\"w\":10,\"h\":10}";
    CHECK(Parse(Cmds(clips), s, e) && s.cmds.size() == 8);
    CHECK(!Parse(Cmds(clips + ",{\"t\":\"clip\",\"kind\":\"rect\",\"x\":0,\"y\":0,\"w\":10,\"h\":10}"), s, e) && e.code == "depth_exceeded");
    // Balanced clips never exceed the depth: 100 push/pop pairs of clips are fine.
    std::string cycles;
    for (int i = 0; i < 100; ++i) cycles += std::string(i ? "," : "") + "{\"t\":\"clip\",\"kind\":\"circle\",\"cx\":0,\"cy\":0,\"r\":5},{\"t\":\"unclip\"}";
    CHECK(Parse(Cmds(cycles), s, e) && s.cmds.size() == 200);
    // Transform depth 16 passes, 17 is refused.
    std::string pushes;
    for (int i = 0; i < 16; ++i) pushes += std::string(i ? "," : "") + "{\"t\":\"push\",\"tx\":1}";
    CHECK(Parse(Cmds(pushes), s, e));
    CHECK(!Parse(Cmds(pushes + ",{\"t\":\"push\"}"), s, e) && e.code == "depth_exceeded");
}

TEST(scene_forward_compat, "scene.unknown-and-malformed-commands") {
    Scene s;
    SceneError e;
    const std::string text = Cmds(
        "{\"t\":\"sparkle\",\"x\":1},"                              // unknown: skipped and counted
        "{\"t\":\"rect\",\"x\":1,\"y\":1,\"w\":5,\"h\":5},"          // fine
        "{\"t\":\"rect\",\"x\":1,\"y\":1,\"w\":5},"                  // missing h: skipped
        "{\"t\":\"rect\",\"x\":\"1\",\"y\":1,\"w\":5,\"h\":5},"      // wrong type: skipped
        "{\"t\":\"rect\",\"x\":1,\"y\":1,\"w\":5,\"h\":5,\"fill\":\"red\"}," // bad colour: skipped
        "{\"t\":\"rect\",\"x\":1,\"y\":1,\"w\":5,\"h\":5,\"fill\":\"#12345\"},"
        "{\"t\":\"rect\",\"x\":1e9,\"y\":1,\"w\":5,\"h\":5},"        // coordinate out of range: skipped
        "{\"t\":\"text\",\"x\":0,\"y\":0,\"size\":10},"              // no text: skipped
        "{\"t\":\"poly\",\"pts\":[1,2,3]},"                          // odd number of values: skipped
        "{\"t\":\"image\",\"x\":0,\"y\":0,\"w\":1,\"h\":1},"         // no id: skipped
        "{\"t\":\"clip\",\"kind\":\"hexagon\"},"                     // unknown clip kind: skipped
        "{\"t\":\"unclip\"},{\"t\":\"pop\"},"                        // nothing to pop: skipped
        "42,\"str\",null,{\"x\":1},"                                 // not commands
        "{\"t\":\"line\",\"x1\":0,\"y1\":0,\"x2\":5,\"y2\":5,\"color\":\"#ff0000\",\"sw\":2}");
    CHECK(Parse(text, s, e));
    CHECK(s.unknownCommands == 1);
    CHECK(s.skippedCommands == 15);
    CHECK(s.cmds.size() == 2 && s.cmds[0].kind == CmdKind::Rect && s.cmds[1].kind == CmdKind::Line);
}

TEST(scene_values, "scene.values") {
    Scene s;
    SceneError e;
    CHECK(Parse(Cmds("{\"t\":\"rect\",\"x\":1,\"y\":2,\"w\":3,\"h\":4,\"fill\":\"#A1b2C3\",\"stroke\":\"#01020304\",\"sw\":2.5,\"r\":6}"), s, e));
    const Cmd& r = s.cmds[0];
    CHECK(r.fill.r == 0xA1 && r.fill.g == 0xB2 && r.fill.b == 0xC3 && r.fill.a == 0xFF);
    CHECK(r.stroke.r == 1 && r.stroke.a == 4 && r.sw == 2.5f && r.r == 6);
    CHECK(Parse(Cmds("{\"t\":\"text\",\"x\":0,\"y\":0,\"s\":\"\\u00e9\\u20ac\",\"size\":12,\"font\":\"black\",\"align\":\"r\",\"base\":\"alpha\",\"maxw\":50,\"outline\":\"#000000ff\"}"), s, e));
    const Cmd& tx = s.cmds[0];
    CHECK(tx.font == FontRole::Black && tx.align == Align::Right && tx.base == Base::Alpha && tx.maxw == 50 && tx.hasOutline && tx.text == "\xC3\xA9\xE2\x82\xAC");
    CHECK(Parse(Cmds("{\"t\":\"text\",\"x\":0,\"y\":0,\"s\":\"a\",\"size\":12,\"font\":\"future-role\"}"), s, e) && s.cmds[0].font == FontRole::Regular);
    CHECK(Parse(Cmds("{\"t\":\"text\",\"x\":0,\"y\":0,\"s\":\"a\",\"size\":0}"), s, e) && s.cmds.empty() && s.skippedCommands == 1);
    CHECK(Parse(Cmds("{\"t\":\"image\",\"id\":\"a\",\"x\":0,\"y\":0,\"w\":4,\"h\":4,\"op\":7,\"tint\":\"#ffffff80\",\"rot\":90}"), s, e));
    CHECK(s.cmds[0].op == 1 && s.cmds[0].hasTint && s.cmds[0].tint.a == 0x80 && s.cmds[0].rot == 90);
    CHECK(Parse(Cmds("{\"t\":\"arc\",\"cx\":0,\"cy\":0,\"r\":5,\"a0\":0,\"a1\":90,\"color\":\"#ffffff\",\"sw\":1,\"cap\":\"square\"}"), s, e) && s.cmds.empty());
}
