#pragma once
// A tiny test harness: tests register themselves, throw on failure, and report
// PASS / FAIL / SKIP lines. A skipped test always says why; nothing skips silently.
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace t {

struct Failure {
    std::string message;
};
struct Skip {
    std::string reason;
};

using TestFn = void (*)();
struct Registration {
    Registration(const char* name, TestFn fn);
};
struct Entry {
    const char* name;
    TestFn fn;
};
std::vector<Entry>& Registry();

// Command line configuration shared by the tests.
struct Config {
    std::string vectorsDirectory;
};
Config& GetConfig();

std::string ReadFile(const std::string& path, bool& ok);

} // namespace t

#define TEST(id, name) \
    static void test_##id(); \
    static t::Registration registration_##id(name, test_##id); \
    static void test_##id()

#define CHECK(cond) \
    do { if (!(cond)) throw t::Failure{std::string(__FILE__) + ":" + std::to_string(__LINE__) + ": " + #cond}; } while (0)

#define CHECK_MSG(cond, message) \
    do { if (!(cond)) throw t::Failure{std::string(__FILE__) + ":" + std::to_string(__LINE__) + ": " + #cond + " (" + (message) + ")"}; } while (0)

#define SKIP(reason) throw t::Skip{reason}
