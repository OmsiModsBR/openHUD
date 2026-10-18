#include "Harness.h"

#include <cstring>
#include <exception>

namespace t {

std::vector<Entry>& Registry() {
    static std::vector<Entry> registry;
    return registry;
}

Registration::Registration(const char* name, TestFn fn) { Registry().push_back({name, fn}); }

Config& GetConfig() {
    static Config config;
    return config;
}

std::string ReadFile(const std::string& path, bool& ok) {
    ok = false;
    FILE* f = nullptr;
    if (fopen_s(&f, path.c_str(), "rb") != 0 || f == nullptr) return {};
    std::string data;
    char buffer[8192];
    size_t n;
    while ((n = fread(buffer, 1, sizeof(buffer), f)) > 0) data.append(buffer, n);
    fclose(f);
    ok = true;
    return data;
}

} // namespace t

int main(int argc, char** argv) {
    std::string filter;
    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--vectors") == 0 && i + 1 < argc) t::GetConfig().vectorsDirectory = argv[++i];
        else if (std::strcmp(argv[i], "--filter") == 0 && i + 1 < argc) filter = argv[++i];
    }
    int passed = 0, failed = 0, skipped = 0;
    for (const t::Entry& entry : t::Registry()) {
        if (!filter.empty() && std::string(entry.name).find(filter) == std::string::npos) continue;
        try {
            entry.fn();
            std::printf("PASS %s\n", entry.name);
            ++passed;
        } catch (const t::Skip& skip) {
            std::printf("SKIP %s: %s\n", entry.name, skip.reason.c_str());
            ++skipped;
        } catch (const t::Failure& failure) {
            std::printf("FAIL %s: %s\n", entry.name, failure.message.c_str());
            ++failed;
        } catch (const std::exception& e) {
            std::printf("FAIL %s: exception: %s\n", entry.name, e.what());
            ++failed;
        } catch (...) {
            std::printf("FAIL %s: unknown exception\n", entry.name);
            ++failed;
        }
        std::fflush(stdout);
    }
    std::printf("native tests: %d passed, %d failed, %d skipped\n", passed, failed, skipped);
    return failed == 0 ? 0 : 1;
}
