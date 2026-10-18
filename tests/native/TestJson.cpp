#include "Harness.h"

#include "Base64.h"
#include "Json.h"
#include "Scene.h"

#include <chrono>
#include <cmath>
#include <cstdint>
#include <string>

using namespace hud;

namespace {

bool Parses(const std::string& text, json::Value* out = nullptr) {
    json::Value v;
    json::Limits limits;
    const bool ok = json::Parse(text, limits, v).ok;
    if (out != nullptr) *out = v;
    return ok;
}

std::string Repeat(const std::string& s, int n) {
    std::string out;
    for (int i = 0; i < n; ++i) out += s;
    return out;
}

// xorshift: deterministic input for the stress tests.
struct Rng {
    uint64_t state;
    explicit Rng(uint64_t seed) : state(seed ? seed : 88172645463325252ull) {}
    uint32_t Next() {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return static_cast<uint32_t>(state >> 11);
    }
};

} // namespace

TEST(json_basic, "json.basic") {
    json::Value v;
    CHECK(Parses(R"({"a":1,"b":[true,false,null,"x"],"c":{"d":-2.5e1}})", &v));
    CHECK(v.type == json::Type::Object && v.members.size() == 3);
    CHECK(v.Find("a")->number == 1);
    CHECK(v.Find("b")->items.size() == 4 && v.Find("b")->items[0].boolean && !v.Find("b")->items[1].boolean);
    CHECK(v.Find("b")->items[2].type == json::Type::Null && v.Find("b")->items[3].text == "x");
    CHECK(v.Find("c")->Find("d")->number == -25.0);
    CHECK(Parses("  [ ]  "));
    CHECK(Parses("{}"));
    CHECK(Parses("0") && Parses("-0") && Parses("\"\""));
}

TEST(json_numbers, "json.numbers") {
    const char* good[] = {"0", "-0", "1", "-1", "10", "1.5", "1e3", "1E+3", "1e-3", "0.5", "123456789012", "1.0e0"};
    for (const char* g : good) CHECK_MSG(Parses(g), g);
    // NaN, Infinity and every non-JSON number spelling are refused.
    const char* bad[] = {"NaN", "nan", "Infinity", "-Infinity", "+1", "01", "-01", ".5", "1.", "1e", "1e+", "--1", "0x10", "1e999", "-1e999", "1_0", "1,5", ""};
    for (const char* b : bad) CHECK_MSG(!Parses(b), b);
    CHECK(!Parses("[NaN]") && !Parses("{\"a\":Infinity}"));
    CHECK(!Parses("1" + std::string(80, '0')));   // absurdly long literal
}

TEST(json_strings, "json.strings") {
    json::Value v;
    CHECK(Parses(R"("a\"b\\c\/d\b\f\n\r\t\u0041\u00e9")", &v));
    CHECK(v.text == std::string("a\"b\\c/d\b\f\n\r\tA\xC3\xA9"));
    CHECK(Parses(R"("\ud83d\ude00")", &v) && v.text == "\xF0\x9F\x98\x80");   // surrogate pair
    CHECK(Parses("\"\xC3\xA9\xE2\x82\xAC\xF0\x9F\x98\x80\"", &v));            // raw UTF-8
    // Lone surrogates, bad escapes, controls, NUL.
    CHECK(!Parses(R"("\ud83d")") && !Parses(R"("\ude00")") && !Parses(R"("\ud83d\u0041")"));
    CHECK(!Parses(R"("\x41")") && !Parses(R"("\u12")") && !Parses(R"("\uZZZZ")") && !Parses(R"("\u0000")"));
    CHECK(!Parses(std::string("\"a\nb\"")) && !Parses(std::string("\"a\tb\"")) && !Parses(std::string("\"a") + '\0' + "b\""));
    CHECK(!Parses("\"abc") && !Parses("\"abc\\"));
}

TEST(json_utf8, "json.invalid-utf8") {
    const char* bad[] = {
        "\"\x80\"",              // continuation byte alone
        "\"\xC0\x80\"",          // overlong NUL
        "\"\xC1\xBF\"",          // overlong
        "\"\xE0\x80\x80\"",      // overlong 3-byte
        "\"\xF0\x80\x80\x80\"",  // overlong 4-byte
        "\"\xED\xA0\x80\"",      // UTF-16 surrogate encoded in UTF-8
        "\"\xF4\x90\x80\x80\"",  // above U+10FFFF
        "\"\xF5\x80\x80\x80\"",  // invalid lead
        "\"\xC3\"",              // truncated
        "\"\xE2\x82\"",          // truncated
        "\"\xC3\x28\"",         // bad continuation
        "\"\xFF\"",
    };
    for (const char* b : bad) CHECK_MSG(!Parses(b), "invalid UTF-8 accepted");
    CHECK(!Parses("\xEF\xBB\xBF{}"));   // a byte order mark is not JSON
    CHECK(!Parses("{\"\xC3\":1}"));     // keys are validated like any string
}

TEST(json_structure, "json.structure") {
    const char* bad[] = {"{", "[", "{\"a\"}", "{\"a\":}", "{\"a\":1,}", "[1,]", "[,1]", "{,}", "{'a':1}", "{a:1}", "[1 2]", "{\"a\":1 \"b\":2}",
                         "[1] x", "{} {}", "tru", "nul", "falsey", "/*c*/{}", "{\"a\":1}//c", "[1,2", "{\"a\":[}", "\"a\" \"b\""};
    for (const char* b : bad) CHECK_MSG(!Parses(b), b);
    CHECK(!Parses("{\"a\":1,\"a\":2}"));     // duplicate keys are refused, never "last wins"
    CHECK(Parses("{\"a\":1,\"b\":2}"));
}

TEST(json_limits, "json.limits") {
    // Depth: 32 nested arrays pass, 33 do not.
    CHECK(Parses(Repeat("[", 32) + Repeat("]", 32)));
    CHECK(!Parses(Repeat("[", 33) + Repeat("]", 33)));
    CHECK(!Parses(Repeat("[", 200000)));                          // unterminated and enormous: refused without recursion blow-up
    CHECK(!Parses(Repeat("{\"a\":", 40) + "1" + Repeat("}", 40)));
    // Size.
    json::Limits small;
    small.maxBytes = 16;
    json::Value v;
    CHECK(json::Parse("[1,2,3]", small, v).ok);
    CHECK(!json::Parse("[1,2,3,4,5,6,7,8,9,10]", small, v).ok);
    // Node count.
    json::Limits nodes;
    nodes.maxNodes = 10;
    CHECK(json::Parse("[1,2,3,4,5,6,7,8,9]", nodes, v).ok);
    CHECK(!json::Parse("[1,2,3,4,5,6,7,8,9,10,11,12]", nodes, v).ok);
    // The default document limit is 1 MiB.
    std::string big = "[" + Repeat("1,", 600000) + "1]";
    CHECK(!Parses(big));
    // A failed parse leaves nothing behind.
    CHECK(!json::Parse("{\"a\":[1,", json::Limits(), v).ok && v.type == json::Type::Null);
}

TEST(base64, "json.base64") {
    std::vector<uint8_t> out;
    CHECK(Base64Decode("AAECAw==", 16, out) && out == std::vector<uint8_t>({0, 1, 2, 3}));
    CHECK(Base64Decode("", 16, out) && out.empty());
    CHECK(Base64Decode("/+8=", 16, out) && out.size() == 2 && out[0] == 0xFF && out[1] == 0xEF);
    CHECK(!Base64Decode("AAECAw=", 16, out));        // length not a multiple of four
    CHECK(!Base64Decode("AAEC Aw==", 16, out));      // whitespace
    CHECK(!Base64Decode("AAE*Aw==", 16, out));       // outside the alphabet
    CHECK(!Base64Decode("AA==AAAA", 16, out));       // padding in the middle
    CHECK(!Base64Decode("AB==", 16, out));           // non-canonical trailing bits
    CHECK(!Base64Decode("AAECAw==", 3, out));        // over the limit
    CHECK(!Base64Decode("====", 16, out));
}

TEST(json_fuzz_garbage, "json.stress-random-garbage") {
    Rng rng(0x9E3779B97F4A7C15ull);
    const auto start = std::chrono::steady_clock::now();
    int accepted = 0;
    for (int i = 0; i < 60000; ++i) {
        const size_t length = rng.Next() % 200;
        std::string text(length, '\0');
        for (char& c : text) c = static_cast<char>(rng.Next());
        json::Value v;
        if (json::Parse(text, json::Limits(), v).ok) ++accepted;
        SceneError error;
        Scene scene;
        ParseScene(text, scene, error);   // must not crash or throw whatever the bytes are
    }
    CHECK_MSG(accepted < 100, "random bytes should almost never be valid JSON");
    // Structured garbage: random JSON-ish tokens.
    const char* tokens[] = {"{", "}", "[", "]", ",", ":", "\"a\"", "\"t\"", "1", "-1.5e3", "true", "null", "\"rect\"", "\"\\u00", "\"\\ud800\"", "NaN"};
    for (int i = 0; i < 40000; ++i) {
        std::string text;
        const int count = static_cast<int>(rng.Next() % 24);
        for (int k = 0; k < count; ++k) text += tokens[rng.Next() % (sizeof(tokens) / sizeof(tokens[0]))];
        json::Value v;
        json::Parse(text, json::Limits(), v);
        Scene scene;
        SceneError error;
        ParseScene(text, scene, error);
    }
    const auto elapsed = std::chrono::duration_cast<std::chrono::seconds>(std::chrono::steady_clock::now() - start).count();
    CHECK_MSG(elapsed < 30, "the parser must stay fast on hostile input");
}

TEST(json_fuzz_mutation, "json.stress-mutated-vectors") {
    // Flip, delete and insert bytes in the real scenes: the result is either a valid scene or a refusal.
    const char* files[] = {"scene-minimal.json", "scene-pause.json", "scene-radar.json"};
    Rng rng(0x1234567ull);
    int refused = 0, accepted = 0;
    for (const char* file : files) {
        bool ok = false;
        const std::string original = t::ReadFile(t::GetConfig().vectorsDirectory + "/" + file, ok);
        if (!ok) SKIP("the golden vectors directory was not given (--vectors)");
        for (int i = 0; i < 4000; ++i) {
            std::string text = original;
            const int edits = 1 + static_cast<int>(rng.Next() % 4);
            for (int e = 0; e < edits && !text.empty(); ++e) {
                const size_t at = rng.Next() % text.size();
                switch (rng.Next() % 3) {
                case 0: text[at] = static_cast<char>(rng.Next()); break;
                case 1: text.erase(at, 1 + rng.Next() % 8); break;
                default: text.insert(at, 1, static_cast<char>(rng.Next())); break;
                }
            }
            Scene scene;
            SceneError error;
            if (ParseScene(text, scene, error)) {
                ++accepted;
                CHECK(scene.cmds.size() <= kMaxCommands);
                for (const Cmd& c : scene.cmds) CHECK(std::isfinite(c.x) && std::isfinite(c.y) && std::isfinite(c.w) && std::isfinite(c.sw));
            } else {
                ++refused;
                CHECK(!error.code.empty());
            }
        }
    }
    CHECK(refused > 100 && accepted > 10);
}
