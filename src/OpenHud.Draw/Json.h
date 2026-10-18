#pragma once
// A small, strict JSON reader for the scene protocol. It exists so the draw layer
// has no dependency and so hostile input is refused in one place: bounded size,
// depth and node count; RFC 8259 grammar only (no NaN, Infinity, comments, trailing
// commas or duplicate keys); strings must be valid UTF-8 and valid escapes.
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace hud::json {

enum class Type : uint8_t { Null, Bool, Number, String, Array, Object };

struct Value {
    Type type = Type::Null;
    bool boolean = false;
    double number = 0;
    std::string text;
    std::vector<Value> items;                           // Array
    std::vector<std::pair<std::string, Value>> members; // Object, in document order

    const Value* Find(std::string_view key) const;
    bool IsNumber() const { return type == Type::Number; }
    bool IsString() const { return type == Type::String; }
};

struct Limits {
    size_t maxBytes = 1u << 20;
    int maxDepth = 32;
    size_t maxNodes = 200000;
};

struct Result {
    bool ok = false;
    std::string error;   // short reason, includes the byte offset
};

Result Parse(std::string_view text, const Limits& limits, Value& out);

// Appends s as a JSON string literal (with quotes), escaping controls and quotes.
void AppendQuoted(std::string& out, std::string_view s);

} // namespace hud::json
