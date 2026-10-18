#include "Scene.h"

#include "Json.h"

#include <cmath>

namespace hud {

namespace {

using json::Type;
using json::Value;

enum class Get { Missing, Ok, Bad };

Get Num(const Value& o, const char* key, float& out) {
    const Value* v = o.Find(key);
    if (v == nullptr) return Get::Missing;
    if (v->type != Type::Number) return Get::Bad;
    if (std::fabs(v->number) > kMaxCoord) return Get::Bad;
    out = static_cast<float>(v->number);
    return Get::Ok;
}

Get Str(const Value& o, const char* key, std::string& out) {
    const Value* v = o.Find(key);
    if (v == nullptr) return Get::Missing;
    if (v->type != Type::String) return Get::Bad;
    out = v->text;
    return Get::Ok;
}

int HexDigit(char c) {
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

bool ParseColor(const std::string& s, Color& out) {
    if ((s.size() != 7 && s.size() != 9) || s[0] != '#') return false;
    uint8_t part[4] = {0, 0, 0, 255};
    for (size_t i = 0; i < (s.size() - 1) / 2; ++i) {
        const int hi = HexDigit(s[1 + i * 2]);
        const int lo = HexDigit(s[2 + i * 2]);
        if (hi < 0 || lo < 0) return false;
        part[i] = static_cast<uint8_t>(hi * 16 + lo);
    }
    out.r = part[0]; out.g = part[1]; out.b = part[2]; out.a = part[3];
    return true;
}

Get Col(const Value& o, const char* key, Color& out) {
    std::string s;
    const Get g = Str(o, key, s);
    if (g != Get::Ok) return g;
    return ParseColor(s, out) ? Get::Ok : Get::Bad;
}

// Required member: any problem skips the command.
#define NEED_NUM(field, key) do { if (Num(o, key, field) != Get::Ok) return false; } while (0)
// Optional member: absent keeps the default, present but invalid skips the command.
#define OPT_NUM(field, key) do { const Get g_ = Num(o, key, field); if (g_ == Get::Bad) return false; } while (0)

bool OptColor(const Value& o, const char* key, bool& has, Color& color) {
    const Get g = Col(o, key, color);
    if (g == Get::Bad) return false;
    has = g == Get::Ok;
    return true;
}

bool ParseCap(const Value& o, Cap& cap) {
    std::string s;
    const Get g = Str(o, "cap", s);
    if (g == Get::Bad) return false;
    if (g == Get::Ok) {
        if (s == "round") cap = Cap::Round;
        else if (s == "butt") cap = Cap::Butt;
        else return false;
    }
    return true;
}

bool OptBool(const Value& o, const char* key, bool& out) {
    const Value* v = o.Find(key);
    if (v == nullptr) return true;
    if (v->type != Type::Bool) return false;
    out = v->boolean;
    return true;
}

FontRole RoleFrom(const std::string& s) {
    if (s == "medium") return FontRole::Medium;
    if (s == "bold") return FontRole::Bold;
    if (s == "black") return FontRole::Black;
    if (s == "condensed") return FontRole::Condensed;
    return FontRole::Regular; // "regular" and anything a newer producer might name
}

// Returns false when the command must be skipped (missing or invalid member).
bool ParseCommand(const std::string& t, const Value& o, Cmd& c) {
    if (t == "rect") {
        c.kind = CmdKind::Rect;
        NEED_NUM(c.x, "x"); NEED_NUM(c.y, "y"); NEED_NUM(c.w, "w"); NEED_NUM(c.h, "h");
        c.sw = 1;
        OPT_NUM(c.sw, "sw"); OPT_NUM(c.r, "r");
        if (c.w < 0 || c.h < 0 || c.sw < 0 || c.r < 0) return false;
        return OptColor(o, "fill", c.hasFill, c.fill) && OptColor(o, "stroke", c.hasStroke, c.stroke);
    }
    if (t == "circle") {
        c.kind = CmdKind::Circle;
        NEED_NUM(c.cx, "cx"); NEED_NUM(c.cy, "cy"); NEED_NUM(c.r, "r");
        c.sw = 1;
        OPT_NUM(c.sw, "sw");
        if (c.r < 0 || c.sw < 0) return false;
        return OptColor(o, "fill", c.hasFill, c.fill) && OptColor(o, "stroke", c.hasStroke, c.stroke);
    }
    if (t == "arc") {
        c.kind = CmdKind::Arc;
        NEED_NUM(c.cx, "cx"); NEED_NUM(c.cy, "cy"); NEED_NUM(c.r, "r");
        NEED_NUM(c.a0, "a0"); NEED_NUM(c.a1, "a1"); NEED_NUM(c.sw, "sw");
        if (c.r < 0 || c.sw < 0) return false;
        if (Col(o, "color", c.stroke) != Get::Ok) return false;
        c.hasStroke = true;
        return ParseCap(o, c.cap);
    }
    if (t == "line") {
        c.kind = CmdKind::Line;
        NEED_NUM(c.x, "x1"); NEED_NUM(c.y, "y1"); NEED_NUM(c.x2, "x2"); NEED_NUM(c.y2, "y2"); NEED_NUM(c.sw, "sw");
        if (c.sw < 0) return false;
        if (Col(o, "color", c.stroke) != Get::Ok) return false;
        c.hasStroke = true;
        return ParseCap(o, c.cap);
    }
    if (t == "poly") {
        c.kind = CmdKind::Poly;
        const Value* pts = o.Find("pts");
        if (pts == nullptr || pts->type != Type::Array) return false;
        if (pts->items.size() < 4 || (pts->items.size() % 2) != 0 || pts->items.size() > kMaxPolyPoints * 2) return false;
        c.pts.reserve(pts->items.size());
        for (const Value& v : pts->items) {
            if (v.type != Type::Number || std::fabs(v.number) > kMaxCoord) return false;
            c.pts.push_back(static_cast<float>(v.number));
        }
        c.sw = 1;
        OPT_NUM(c.sw, "sw");
        if (c.sw < 0) return false;
        if (!OptBool(o, "closed", c.closed)) return false;
        return OptColor(o, "fill", c.hasFill, c.fill) && OptColor(o, "stroke", c.hasStroke, c.stroke);
    }
    if (t == "text") {
        c.kind = CmdKind::Text;
        NEED_NUM(c.x, "x"); NEED_NUM(c.y, "y"); NEED_NUM(c.size, "size");
        if (Str(o, "s", c.text) != Get::Ok) return false;
        if (c.size <= 0 || c.size > kMaxTextSize || c.text.size() > kMaxTextBytes) return false;
        std::string s;
        const Get role = Str(o, "font", s);
        if (role == Get::Bad) return false;
        if (role == Get::Ok) c.font = RoleFrom(s);
        c.fill = Color();
        bool hasColor = false;
        if (!OptColor(o, "color", hasColor, c.fill)) return false;
        const Get align = Str(o, "align", s);
        if (align == Get::Bad) return false;
        if (align == Get::Ok) c.align = s == "c" ? Align::Center : (s == "r" ? Align::Right : Align::Left);
        const Get base = Str(o, "base", s);
        if (base == Get::Bad) return false;
        if (base == Get::Ok) c.base = s == "mid" ? Base::Mid : (s == "alpha" ? Base::Alpha : Base::Top);
        OPT_NUM(c.maxw, "maxw");
        c.hasFill = true;
        return OptColor(o, "outline", c.hasOutline, c.outline);
    }
    if (t == "image") {
        c.kind = CmdKind::Image;
        if (Str(o, "id", c.text) != Get::Ok || c.text.empty() || c.text.size() > kMaxImageIdBytes) return false;
        NEED_NUM(c.x, "x"); NEED_NUM(c.y, "y"); NEED_NUM(c.w, "w"); NEED_NUM(c.h, "h");
        if (c.w <= 0 || c.h <= 0) return false;
        OPT_NUM(c.op, "op"); OPT_NUM(c.rot, "rot");
        c.op = c.op < 0 ? 0 : (c.op > 1 ? 1 : c.op);
        return OptColor(o, "tint", c.hasTint, c.tint);
    }
    if (t == "clip") {
        c.kind = CmdKind::Clip;
        std::string kind;
        if (Str(o, "kind", kind) != Get::Ok) return false;
        if (kind == "rect") {
            NEED_NUM(c.x, "x"); NEED_NUM(c.y, "y"); NEED_NUM(c.w, "w"); NEED_NUM(c.h, "h");
            return c.w >= 0 && c.h >= 0;
        }
        if (kind == "circle") {
            c.clipCircle = true;
            NEED_NUM(c.cx, "cx"); NEED_NUM(c.cy, "cy"); NEED_NUM(c.r, "r");
            return c.r >= 0;
        }
        return false;
    }
    if (t == "unclip") { c.kind = CmdKind::Unclip; return true; }
    if (t == "push") {
        c.kind = CmdKind::Push;
        OPT_NUM(c.tx, "tx"); OPT_NUM(c.ty, "ty"); OPT_NUM(c.rot, "rot"); OPT_NUM(c.sx, "sx"); OPT_NUM(c.sy, "sy");
        return true;
    }
    if (t == "pop") { c.kind = CmdKind::Pop; return true; }
    return false;
}

bool KnownCommand(const std::string& t) {
    static const char* const names[] = {"rect", "circle", "arc", "line", "poly", "text", "image", "clip", "unclip", "push", "pop"};
    for (const char* n : names) {
        if (t == n) return true;
    }
    return false;
}

bool Refuse(SceneError& e, const char* code, const std::string& detail) {
    e.code = code;
    e.detail = detail;
    return false;
}

} // namespace

const char* RoleName(FontRole role) {
    switch (role) {
    case FontRole::Medium: return "medium";
    case FontRole::Bold: return "bold";
    case FontRole::Black: return "black";
    case FontRole::Condensed: return "condensed";
    default: return "regular";
    }
}

bool ParseScene(std::string_view text, Scene& out, SceneError& error) {
    if (text.size() > kMaxSceneBytes) return Refuse(error, "too_large", "the scene exceeds 1 MiB");
    json::Value root;
    json::Limits limits;
    limits.maxBytes = kMaxSceneBytes;
    const json::Result parsed = json::Parse(text, limits, root);
    if (!parsed.ok) return Refuse(error, "json_syntax", parsed.error);
    if (root.type != Type::Object) return Refuse(error, "bad_scene", "the scene must be a JSON object");

    Scene scene;
    const Value* version = root.Find("v");
    const Value* cmds = root.Find("cmds");
    if (version == nullptr && cmds == nullptr && root.members.empty()) {
        out = std::move(scene);   // {} draws nothing
        return true;
    }
    if (version == nullptr || version->type != Type::Number || version->number != 1.0)
        return Refuse(error, "unsupported_version", "v must be 1");

    float w = 1920, h = 1080;
    if (Num(root, "w", w) == Get::Bad || Num(root, "h", h) == Get::Bad) return Refuse(error, "bad_scene", "w and h must be numbers");
    if (w < 1 || h < 1 || w > kMaxCanvas || h > kMaxCanvas) return Refuse(error, "bad_scene", "the canvas size is out of range");
    scene.w = static_cast<int>(w);
    scene.h = static_cast<int>(h);
    const Value* background = root.Find("background");
    if (background != nullptr) {
        if (background->type != Type::Bool) return Refuse(error, "bad_scene", "background must be a boolean");
        scene.background = background->boolean;
    }
    if (cmds != nullptr) {
        if (cmds->type != Type::Array) return Refuse(error, "bad_scene", "cmds must be an array");
        if (cmds->items.size() > kMaxCommands) return Refuse(error, "too_many_commands", "more than 8192 commands");
        scene.cmds.reserve(cmds->items.size());
        int clipDepth = 0, pushDepth = 0;
        for (const Value& item : cmds->items) {
            std::string t;
            if (item.type != Type::Object || Str(item, "t", t) != Get::Ok) { ++scene.skippedCommands; continue; }
            if (!KnownCommand(t)) { ++scene.unknownCommands; continue; }
            Cmd cmd;
            if (!ParseCommand(t, item, cmd)) { ++scene.skippedCommands; continue; }
            switch (cmd.kind) {
            case CmdKind::Clip:
                if (++clipDepth > kMaxClipDepth) return Refuse(error, "depth_exceeded", "clip nesting deeper than 8");
                break;
            case CmdKind::Push:
                if (++pushDepth > kMaxPushDepth) return Refuse(error, "depth_exceeded", "transform nesting deeper than 16");
                break;
            case CmdKind::Unclip:
                if (clipDepth == 0) { ++scene.skippedCommands; continue; }
                --clipDepth;
                break;
            case CmdKind::Pop:
                if (pushDepth == 0) { ++scene.skippedCommands; continue; }
                --pushDepth;
                break;
            default: break;
            }
            scene.cmds.push_back(std::move(cmd));
        }
    }
    out = std::move(scene);
    return true;
}

} // namespace hud
