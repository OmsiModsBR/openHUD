#pragma once
// The scene model: what docs/scene-protocol.md describes, after validation. The
// parser enforces every limit of the protocol, so the renderer can trust a Scene.
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace hud {

// Limits of the protocol (both sides keep them).
constexpr size_t kMaxSceneBytes = 1u << 20;
constexpr size_t kMaxCommands = 8192;
constexpr int kMaxClipDepth = 8;
constexpr int kMaxPushDepth = 16;
constexpr size_t kMaxImages = 128;
constexpr size_t kMaxImageBytes = 4u << 20;
// Implementation limits that protect the host process (documented in the report).
constexpr size_t kMaxPolyPoints = 4096;
constexpr size_t kMaxTextBytes = 4096;
constexpr size_t kMaxImageIdBytes = 64;
constexpr double kMaxCoord = 1.0e6;
constexpr double kMaxTextSize = 1024.0;
constexpr int kMaxCanvas = 16384;

struct Color {
    uint8_t r = 255, g = 255, b = 255, a = 255;
    uint32_t Argb() const {
        return (static_cast<uint32_t>(a) << 24) | (static_cast<uint32_t>(r) << 16) | (static_cast<uint32_t>(g) << 8) | b;
    }
};

enum class CmdKind : uint8_t { Rect, Circle, Arc, Line, Poly, Text, Image, Clip, Unclip, Push, Pop };
enum class Cap : uint8_t { Butt, Round };
enum class Align : uint8_t { Left, Center, Right };
enum class Base : uint8_t { Top, Mid, Alpha };
enum class FontRole : uint8_t { Regular, Medium, Bold, Black, Condensed };

struct Cmd {
    CmdKind kind = CmdKind::Rect;
    // rect / image / clip rect: x y w h; text and line: x y. r: rect corner radius, circle and arc radius.
    float x = 0, y = 0, w = 0, h = 0, r = 0;
    float cx = 0, cy = 0;     // circle, arc, clip circle
    float x2 = 0, y2 = 0;     // line end
    float a0 = 0, a1 = 0;     // arc angles, degrees
    float sw = 0;             // stroke width
    bool hasFill = false, hasStroke = false;
    Color fill, stroke;       // text colour is `fill`; arc and line colour is `stroke`
    Cap cap = Cap::Butt;
    bool closed = true;       // poly
    bool clipCircle = false;  // clip
    std::vector<float> pts;   // poly
    std::string text;         // text content or image id
    float size = 0;           // text em size
    float maxw = 0;           // text clip width, <= 0 for none
    FontRole font = FontRole::Regular;
    Align align = Align::Left;
    Base base = Base::Top;
    bool hasOutline = false;
    Color outline;
    float op = 1;             // image opacity
    bool hasTint = false;
    Color tint;
    float rot = 0;            // image and push rotation, degrees
    float tx = 0, ty = 0, sx = 1, sy = 1;
};

struct Scene {
    int w = 1920, h = 1080;
    bool background = false;
    std::vector<Cmd> cmds;
    int unknownCommands = 0;   // commands with an unknown "t" (skipped)
    int skippedCommands = 0;   // known commands with a missing or invalid member (skipped)
};

struct SceneError {
    std::string code;    // json_syntax, too_large, unsupported_version, bad_scene, too_many_commands, depth_exceeded
    std::string detail;
};

// Parses and validates a scene document. On failure `out` is untouched.
bool ParseScene(std::string_view json, Scene& out, SceneError& error);

const char* RoleName(FontRole role);

} // namespace hud
