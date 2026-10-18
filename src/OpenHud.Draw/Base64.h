#pragma once
// Strict base64 (RFC 4648, standard alphabet, mandatory padding, no whitespace).
#include <cstdint>
#include <string_view>
#include <vector>

namespace hud {

// Decodes into `out`. Returns false for any character outside the alphabet, bad
// padding, or a decoded size over maxBytes (checked before allocating).
inline bool Base64Decode(std::string_view in, size_t maxBytes, std::vector<uint8_t>& out) {
    out.clear();
    if (in.size() % 4 != 0) return false;
    if (in.empty()) return true;
    size_t pad = 0;
    if (in[in.size() - 1] == '=') ++pad;
    if (in.size() > 1 && in[in.size() - 2] == '=') ++pad;
    const size_t decoded = in.size() / 4 * 3 - pad;
    if (decoded > maxBytes) return false;
    out.reserve(decoded);
    uint32_t acc = 0;
    int bits = 0;
    for (size_t i = 0; i < in.size() - pad; ++i) {
        const char c = in[i];
        int v;
        if (c >= 'A' && c <= 'Z') v = c - 'A';
        else if (c >= 'a' && c <= 'z') v = c - 'a' + 26;
        else if (c >= '0' && c <= '9') v = c - '0' + 52;
        else if (c == '+') v = 62;
        else if (c == '/') v = 63;
        else { out.clear(); return false; }
        acc = (acc << 6) | static_cast<uint32_t>(v);
        bits += 6;
        if (bits >= 8) {
            bits -= 8;
            out.push_back(static_cast<uint8_t>((acc >> bits) & 0xFF));
        }
    }
    // The unused low bits of the last group must be zero (canonical encoding).
    if (bits > 0 && (acc & ((1u << bits) - 1)) != 0) { out.clear(); return false; }
    return out.size() == decoded;
}

} // namespace hud
