#include "Json.h"

#include <charconv>
#include <cmath>

namespace hud::json {

const Value* Value::Find(std::string_view key) const {
    if (type != Type::Object) return nullptr;
    for (const auto& member : members) {
        if (member.first == key) return &member.second;
    }
    return nullptr;
}

namespace {

class Parser {
public:
    Parser(std::string_view text, const Limits& limits) : s_(text), limits_(limits) {}

    Result Run(Value& out) {
        if (s_.size() > limits_.maxBytes) return Fail("document too large");
        SkipSpace();
        if (!ParseValue(out, 1)) return Fail(error_.empty() ? "syntax error" : error_.c_str());
        SkipSpace();
        if (pos_ != s_.size()) return Fail("trailing data");
        Result ok;
        ok.ok = true;
        return ok;
    }

private:
    std::string_view s_;
    const Limits& limits_;
    size_t pos_ = 0;
    size_t nodes_ = 0;
    std::string error_;

    Result Fail(const char* what) const {
        Result r;
        r.ok = false;
        r.error = std::string(what) + " at byte " + std::to_string(pos_);
        return r;
    }

    bool Err(const char* what) {
        if (error_.empty()) error_ = what;
        return false;
    }

    void SkipSpace() {
        while (pos_ < s_.size()) {
            const char c = s_[pos_];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r') ++pos_;
            else break;
        }
    }

    bool ParseValue(Value& out, int depth) {
        if (depth > limits_.maxDepth) return Err("nesting too deep");
        if (++nodes_ > limits_.maxNodes) return Err("too many values");
        if (pos_ >= s_.size()) return Err("unexpected end");
        const char c = s_[pos_];
        switch (c) {
        case '{': return ParseObject(out, depth);
        case '[': return ParseArray(out, depth);
        case '"': out.type = Type::String; return ParseString(out.text);
        case 't': return Literal("true", out, Type::Bool, true);
        case 'f': return Literal("false", out, Type::Bool, false);
        case 'n': return Literal("null", out, Type::Null, false);
        default:
            if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(out);
            return Err("unexpected character");
        }
    }

    bool Literal(const char* word, Value& out, Type type, bool boolean) {
        const std::string_view w(word);
        if (s_.substr(pos_, w.size()) != w) return Err("bad literal");
        pos_ += w.size();
        out.type = type;
        out.boolean = boolean;
        return true;
    }

    bool ParseNumber(Value& out) {
        const size_t start = pos_;
        if (s_[pos_] == '-') ++pos_;
        if (pos_ >= s_.size()) return Err("bad number");
        if (s_[pos_] == '0') {
            ++pos_;
        } else if (s_[pos_] >= '1' && s_[pos_] <= '9') {
            while (pos_ < s_.size() && s_[pos_] >= '0' && s_[pos_] <= '9') ++pos_;
        } else {
            return Err("bad number");
        }
        if (pos_ < s_.size() && s_[pos_] == '.') {
            ++pos_;
            const size_t digits = pos_;
            while (pos_ < s_.size() && s_[pos_] >= '0' && s_[pos_] <= '9') ++pos_;
            if (pos_ == digits) return Err("bad number");
        }
        if (pos_ < s_.size() && (s_[pos_] == 'e' || s_[pos_] == 'E')) {
            ++pos_;
            if (pos_ < s_.size() && (s_[pos_] == '+' || s_[pos_] == '-')) ++pos_;
            const size_t digits = pos_;
            while (pos_ < s_.size() && s_[pos_] >= '0' && s_[pos_] <= '9') ++pos_;
            if (pos_ == digits) return Err("bad number");
        }
        if (pos_ - start > 64) return Err("number too long");
        double value = 0;
        const auto res = std::from_chars(s_.data() + start, s_.data() + pos_, value);
        if (res.ec == std::errc::result_out_of_range) return Err("number out of range");
        if (res.ec != std::errc() || res.ptr != s_.data() + pos_) return Err("bad number");
        if (!std::isfinite(value)) return Err("number not finite");
        out.type = Type::Number;
        out.number = value;
        return true;
    }

    static int Hex(char c) {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }

    bool ReadHex4(unsigned& unit) {
        if (pos_ + 4 > s_.size()) return Err("bad escape");
        unit = 0;
        for (size_t i = 0; i < 4; ++i) {
            const int h = Hex(s_[pos_ + i]);
            if (h < 0) return Err("bad escape");
            unit = unit * 16 + static_cast<unsigned>(h);
        }
        pos_ += 4;
        return true;
    }

    static void AppendUtf8(std::string& out, unsigned cp) {
        if (cp < 0x80) {
            out.push_back(static_cast<char>(cp));
        } else if (cp < 0x800) {
            out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else if (cp < 0x10000) {
            out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else {
            out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
    }

    // Validates one UTF-8 sequence starting at pos_ (lead byte >= 0x80) and copies it.
    bool CopyUtf8(std::string& out) {
        const auto byte = [&](size_t i) { return static_cast<unsigned char>(s_[i]); };
        const unsigned char lead = byte(pos_);
        size_t length;
        unsigned cp;
        if (lead >= 0xC2 && lead <= 0xDF) { length = 2; cp = lead & 0x1Fu; }
        else if (lead >= 0xE0 && lead <= 0xEF) { length = 3; cp = lead & 0x0Fu; }
        else if (lead >= 0xF0 && lead <= 0xF4) { length = 4; cp = lead & 0x07u; }
        else return Err("invalid UTF-8");
        if (pos_ + length > s_.size()) return Err("invalid UTF-8");
        for (size_t i = 1; i < length; ++i) {
            const unsigned char b = byte(pos_ + i);
            if ((b & 0xC0) != 0x80) return Err("invalid UTF-8");
            cp = (cp << 6) | (b & 0x3Fu);
        }
        if ((length == 3 && cp < 0x800) || (length == 4 && cp < 0x10000)) return Err("invalid UTF-8");
        if (cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) return Err("invalid UTF-8");
        out.append(s_.data() + pos_, length);
        pos_ += length;
        return true;
    }

    bool ParseString(std::string& out) {
        out.clear();
        ++pos_; // opening quote
        while (true) {
            if (pos_ >= s_.size()) return Err("unterminated string");
            const unsigned char c = static_cast<unsigned char>(s_[pos_]);
            if (c == '"') { ++pos_; return true; }
            if (c < 0x20) return Err("control character in string");
            if (c >= 0x80) {
                if (!CopyUtf8(out)) return false;
                continue;
            }
            if (c != '\\') { out.push_back(static_cast<char>(c)); ++pos_; continue; }
            ++pos_;
            if (pos_ >= s_.size()) return Err("bad escape");
            const char e = s_[pos_++];
            switch (e) {
            case '"': out.push_back('"'); break;
            case '\\': out.push_back('\\'); break;
            case '/': out.push_back('/'); break;
            case 'b': out.push_back('\b'); break;
            case 'f': out.push_back('\f'); break;
            case 'n': out.push_back('\n'); break;
            case 'r': out.push_back('\r'); break;
            case 't': out.push_back('\t'); break;
            case 'u': {
                unsigned unit;
                if (!ReadHex4(unit)) return false;
                if (unit >= 0xD800 && unit <= 0xDBFF) {
                    if (pos_ + 2 > s_.size() || s_[pos_] != '\\' || s_[pos_ + 1] != 'u') return Err("lone surrogate");
                    pos_ += 2;
                    unsigned low;
                    if (!ReadHex4(low)) return false;
                    if (low < 0xDC00 || low > 0xDFFF) return Err("lone surrogate");
                    unit = 0x10000 + ((unit - 0xD800) << 10) + (low - 0xDC00);
                } else if (unit >= 0xDC00 && unit <= 0xDFFF) {
                    return Err("lone surrogate");
                }
                if (unit == 0) return Err("NUL in string");
                AppendUtf8(out, unit);
                break;
            }
            default: return Err("bad escape");
            }
        }
    }

    bool ParseArray(Value& out, int depth) {
        out.type = Type::Array;
        ++pos_;
        SkipSpace();
        if (pos_ < s_.size() && s_[pos_] == ']') { ++pos_; return true; }
        while (true) {
            out.items.emplace_back();
            SkipSpace();
            if (!ParseValue(out.items.back(), depth + 1)) return false;
            SkipSpace();
            if (pos_ >= s_.size()) return Err("unterminated array");
            if (s_[pos_] == ',') { ++pos_; continue; }
            if (s_[pos_] == ']') { ++pos_; return true; }
            return Err("expected , or ]");
        }
    }

    bool ParseObject(Value& out, int depth) {
        out.type = Type::Object;
        ++pos_;
        SkipSpace();
        if (pos_ < s_.size() && s_[pos_] == '}') { ++pos_; return true; }
        while (true) {
            SkipSpace();
            if (pos_ >= s_.size() || s_[pos_] != '"') return Err("expected key");
            std::string key;
            if (!ParseString(key)) return false;
            for (const auto& m : out.members) {
                if (m.first == key) return Err("duplicate key");
            }
            SkipSpace();
            if (pos_ >= s_.size() || s_[pos_] != ':') return Err("expected :");
            ++pos_;
            SkipSpace();
            out.members.emplace_back(std::move(key), Value());
            if (!ParseValue(out.members.back().second, depth + 1)) return false;
            SkipSpace();
            if (pos_ >= s_.size()) return Err("unterminated object");
            if (s_[pos_] == ',') { ++pos_; continue; }
            if (s_[pos_] == '}') { ++pos_; return true; }
            return Err("expected , or }");
        }
    }
};

} // namespace

Result Parse(std::string_view text, const Limits& limits, Value& out) {
    out = Value();
    Parser parser(text, limits);
    Result r = parser.Run(out);
    if (!r.ok) out = Value();
    return r;
}

void AppendQuoted(std::string& out, std::string_view s) {
    out.push_back('"');
    for (const char ch : s) {
        const unsigned char c = static_cast<unsigned char>(ch);
        switch (c) {
        case '"': out += "\\\""; break;
        case '\\': out += "\\\\"; break;
        case '\n': out += "\\n"; break;
        case '\r': out += "\\r"; break;
        case '\t': out += "\\t"; break;
        default:
            if (c < 0x20) {
                static const char* hex = "0123456789abcdef";
                out += "\\u00";
                out.push_back(hex[c >> 4]);
                out.push_back(hex[c & 15]);
            } else {
                out.push_back(ch);
            }
        }
    }
    out.push_back('"');
}

} // namespace hud::json
