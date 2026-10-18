#include "Fonts.h"

#include <windows.h>

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <unordered_map>

namespace hud {

namespace {

uint16_t Be16(const uint8_t* p) { return static_cast<uint16_t>((p[0] << 8) | p[1]); }
uint32_t Be32(const uint8_t* p) {
    return (static_cast<uint32_t>(p[0]) << 24) | (static_cast<uint32_t>(p[1]) << 16) | (static_cast<uint32_t>(p[2]) << 8) | p[3];
}

constexpr uint32_t Tag(char a, char b, char c, char d) {
    return (static_cast<uint32_t>(static_cast<uint8_t>(a)) << 24) | (static_cast<uint32_t>(static_cast<uint8_t>(b)) << 16) |
           (static_cast<uint32_t>(static_cast<uint8_t>(c)) << 8) | static_cast<uint32_t>(static_cast<uint8_t>(d));
}

constexpr uint64_t kMaxTableBytes = 4u << 20;

struct TableRef {
    uint32_t offset = 0, length = 0;
    bool found = false;
};

std::wstring DecodeName(const uint8_t* s, size_t length, bool utf16) {
    std::wstring out;
    if (utf16) {
        for (size_t i = 0; i + 1 < length; i += 2) out.push_back(static_cast<wchar_t>(Be16(s + i)));
    } else {
        for (size_t i = 0; i < length; ++i) out.push_back(static_cast<wchar_t>(s[i] < 0x80 ? s[i] : '?'));
    }
    return out;
}

} // namespace

bool ReadSfntInfo(const ByteReader& read, uint64_t fileSize, FontFace& face, std::string& error) {
    uint8_t header[16];
    if (fileSize < 12 || !read(0, header, 12)) { error = "not a font file"; return false; }
    uint64_t base = 0;
    uint32_t tag = Be32(header);
    if (tag == Tag('t', 't', 'c', 'f')) {
        uint8_t ttc[16];
        if (fileSize < 16 || !read(0, ttc, 16) || Be32(ttc + 8) == 0) { error = "bad font collection"; return false; }
        base = Be32(ttc + 12);
        if (base + 12 > fileSize || !read(base, header, 12)) { error = "bad font collection"; return false; }
        tag = Be32(header);
    }
    if (tag != 0x00010000u && tag != Tag('O', 'T', 'T', 'O') && tag != Tag('t', 'r', 'u', 'e')) { error = "not a TrueType or OpenType font"; return false; }
    const unsigned tables = Be16(header + 4);
    if (tables == 0 || tables > 256) { error = "bad table directory"; return false; }
    std::vector<uint8_t> dir(static_cast<size_t>(tables) * 16);
    if (base + 12 + dir.size() > fileSize || !read(base + 12, dir.data(), dir.size())) { error = "bad table directory"; return false; }
    TableRef name, os2;
    for (unsigned i = 0; i < tables; ++i) {
        const uint8_t* rec = dir.data() + static_cast<size_t>(i) * 16;
        const uint32_t t = Be32(rec);
        TableRef ref{Be32(rec + 8), Be32(rec + 12), true};
        if (static_cast<uint64_t>(ref.offset) + ref.length > fileSize) continue;
        if (t == Tag('n', 'a', 'm', 'e')) name = ref;
        else if (t == Tag('O', 'S', '/', '2')) os2 = ref;
    }
    if (!name.found || name.length < 6 || name.length > kMaxTableBytes) { error = "the font has no name table"; return false; }
    std::vector<uint8_t> nt(name.length);
    if (!read(name.offset, nt.data(), nt.size())) { error = "unreadable name table"; return false; }
    const unsigned count = Be16(nt.data() + 2), storage = Be16(nt.data() + 4);
    if (6 + static_cast<size_t>(count) * 12 > nt.size()) { error = "bad name table"; return false; }
    int bestScore = -1;
    std::wstring family;
    for (unsigned i = 0; i < count; ++i) {
        const uint8_t* rec = nt.data() + 6 + static_cast<size_t>(i) * 12;
        const unsigned platform = Be16(rec), encoding = Be16(rec + 2), language = Be16(rec + 4), id = Be16(rec + 6);
        const size_t length = Be16(rec + 8), offset = static_cast<size_t>(storage) + Be16(rec + 10);
        if (id != 1 || length == 0 || offset + length > nt.size()) continue;
        int score = -1;
        if (platform == 3 && (encoding == 1 || encoding == 10)) score = language == 0x409 ? 4 : 3;
        else if (platform == 0) score = 2;
        else if (platform == 1 && encoding == 0 && language == 0) score = 1;
        if (score > bestScore) {
            bestScore = score;
            family = DecodeName(nt.data() + offset, length, platform != 1);
        }
    }
    if (family.empty() || family.size() > 31) { error = "the font has no usable family name"; return false; }
    face.family = family;
    face.weight = 400;
    face.italic = false;
    if (os2.found && os2.length >= 64) {
        uint8_t o[64];
        if (read(os2.offset, o, 64)) {
            const unsigned weight = Be16(o + 4);
            if (weight >= 1 && weight <= 1000) face.weight = static_cast<int>(weight);
            face.italic = (Be16(o + 62) & 1u) != 0;
        }
    }
    return true;
}

FontRegistry::~FontRegistry() { Clear(); }

bool FontRegistry::Put(FontRole role, const std::wstring& path, std::string& error) {
    const DWORD attributes = GetFileAttributesW(path.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY)) { error = "the font file does not exist"; return false; }
    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"rb") != 0 || file == nullptr) { error = "the font file cannot be opened"; return false; }
    _fseeki64(file, 0, SEEK_END);
    const long long size = _ftelli64(file);
    FontFace face;
    bool ok = false;
    if (size > 0 && size <= (64ll << 20)) {
        ok = ReadSfntInfo(
            [file](uint64_t offset, void* dst, size_t length) {
                return _fseeki64(file, static_cast<long long>(offset), SEEK_SET) == 0 && fread(dst, 1, length, file) == length;
            },
            static_cast<uint64_t>(size), face, error);
    } else {
        error = "the font file size is not acceptable";
    }
    fclose(file);
    if (!ok) return false;
    face.path = path;
    if (AddFontResourceExW(path.c_str(), FR_PRIVATE, nullptr) == 0) { error = "the system refused the font"; return false; }
    std::lock_guard<std::mutex> guard(lock_);
    const auto it = faces_.find(static_cast<int>(role));
    if (it != faces_.end() && it->second.path != path) RemoveFontResourceExW(it->second.path.c_str(), FR_PRIVATE, nullptr);
    else if (it != faces_.end()) RemoveFontResourceExW(path.c_str(), FR_PRIVATE, nullptr);   // same file again: keep one reference
    faces_[static_cast<int>(role)] = face;
    ++generation_;
    return true;
}

void FontRegistry::Clear() {
    std::lock_guard<std::mutex> guard(lock_);
    for (const auto& entry : faces_) RemoveFontResourceExW(entry.second.path.c_str(), FR_PRIVATE, nullptr);
    faces_.clear();
    ++generation_;
}

int FontRegistry::Count() const {
    std::lock_guard<std::mutex> guard(lock_);
    return static_cast<int>(faces_.size());
}

bool FontRegistry::Get(FontRole role, FontFace& face) const {
    std::lock_guard<std::mutex> guard(lock_);
    const auto it = faces_.find(static_cast<int>(role));
    if (it == faces_.end()) return false;
    face = it->second;
    return true;
}

uint32_t FontRegistry::Generation() const {
    std::lock_guard<std::mutex> guard(lock_);
    return generation_;
}

struct GdiGlyphSource::Impl {
    std::mutex lock;   // one device context: the render thread and a measure call from the module must not interleave
    HDC dc = nullptr;
    uint32_t generation = 0;
    std::unordered_map<uint64_t, HFONT> fonts;     // (kind, role, px)
    std::wstring systemFace;

    ~Impl() {
        DropFonts();
        if (dc != nullptr) DeleteDC(dc);
    }

    void DropFonts() {
        if (dc != nullptr) SelectObject(dc, GetStockObject(SYSTEM_FONT));
        for (auto& entry : fonts) DeleteObject(entry.second);
        fonts.clear();
    }

    const std::wstring& SystemFace() {
        if (systemFace.empty()) {
            NONCLIENTMETRICSW metrics = {};
            metrics.cbSize = sizeof(metrics);
            if (SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, sizeof(metrics), &metrics, 0) && metrics.lfMessageFont.lfFaceName[0] != 0)
                systemFace = metrics.lfMessageFont.lfFaceName;
            else
                systemFace = L"Segoe UI";
        }
        return systemFace;
    }

    static int SystemWeight(FontRole role) {
        switch (role) {
        case FontRole::Medium: return 500;
        case FontRole::Bold: return 700;
        case FontRole::Black: return 900;
        default: return 400;
        }
    }

    HFONT Make(const wchar_t* face, int weight, bool italic, int px) {
        return CreateFontW(-px, 0, 0, 0, weight, italic ? TRUE : FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_TT_ONLY_PRECIS,
                           CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY, DEFAULT_PITCH | FF_DONTCARE, face);
    }

    // kind 0: the font of the role (or of "regular", or the system font); kind 1: the system UI font.
    HFONT Get(const FontRegistry& registry, FontRole role, int px, int kind) {
        const uint32_t gen = registry.Generation();
        if (gen != generation) {
            DropFonts();
            generation = gen;
        }
        const uint64_t key = (static_cast<uint64_t>(kind) << 40) | (static_cast<uint64_t>(role) << 32) | static_cast<uint32_t>(px);
        const auto it = fonts.find(key);
        if (it != fonts.end()) return it->second;
        HFONT font = nullptr;
        if (kind == 0) {
            FontFace face;
            if (registry.Get(role, face) || registry.Get(FontRole::Regular, face)) font = Make(face.family.c_str(), face.weight, face.italic, px);
        }
        if (font == nullptr) font = Make(SystemFace().c_str(), SystemWeight(role), false, px);
        if (font != nullptr) fonts[key] = font;
        return font;
    }
};

GdiGlyphSource::GdiGlyphSource(FontRegistry& registry) : registry_(registry), impl_(new Impl) {
    impl_->dc = CreateCompatibleDC(nullptr);
}

GdiGlyphSource::~GdiGlyphSource() { delete impl_; }

bool GdiGlyphSource::Metrics(FontRole role, int px, FaceMetrics& out) {
    std::lock_guard<std::mutex> guard(impl_->lock);
    if (impl_->dc == nullptr) return false;
    HFONT font = impl_->Get(registry_, role, px, 0);
    if (font == nullptr) return false;
    SelectObject(impl_->dc, font);
    TEXTMETRICW tm;
    if (!GetTextMetricsW(impl_->dc, &tm)) return false;
    out.ascent = static_cast<float>(tm.tmAscent);
    out.descent = static_cast<float>(tm.tmDescent);
    return true;
}

bool GdiGlyphSource::Glyph(FontRole role, int px, uint32_t codepoint, GlyphBitmap& out) {
    out = GlyphBitmap();
    std::lock_guard<std::mutex> guard(impl_->lock);
    if (impl_->dc == nullptr || px < 1) return false;
    wchar_t wch;
    if (codepoint == 0 || codepoint > 0xFFFF || (codepoint >= 0xD800 && codepoint <= 0xDFFF)) {
        // GDI outlines are per UTF-16 unit; characters beyond the BMP draw as a blank of half an em.
        out.advance = static_cast<float>(px) * 0.5f;
        return true;
    }
    wch = static_cast<wchar_t>(codepoint);
    HFONT font = impl_->Get(registry_, role, px, 0);
    if (font == nullptr) return false;
    SelectObject(impl_->dc, font);
    WORD index = 0xFFFF;
    if (GetGlyphIndicesW(impl_->dc, &wch, 1, &index, GGI_MARK_NONEXISTING_GLYPHS) == GDI_ERROR || index == 0xFFFF) {
        HFONT fallback = impl_->Get(registry_, role, px, 1);
        if (fallback != nullptr) SelectObject(impl_->dc, fallback);   // per glyph; the system font draws its own .notdef if it lacks it too
    }
    GLYPHMETRICS gm = {};
    const MAT2 identity = {{0, 1}, {0, 0}, {0, 0}, {0, 1}};
    const DWORD bytes = GetGlyphOutlineW(impl_->dc, wch, GGO_GRAY8_BITMAP, &gm, 0, nullptr, &identity);
    if (bytes == GDI_ERROR) return false;
    out.advance = static_cast<float>(gm.gmCellIncX);
    if (bytes == 0 || gm.gmBlackBoxX == 0 || gm.gmBlackBoxY == 0) return true;   // blank glyph
    if (gm.gmBlackBoxX > 1024 || gm.gmBlackBoxY > 1024) return false;
    std::vector<uint8_t> raw(bytes);
    if (GetGlyphOutlineW(impl_->dc, wch, GGO_GRAY8_BITMAP, &gm, bytes, raw.data(), &identity) == GDI_ERROR) return false;
    const int w = static_cast<int>(gm.gmBlackBoxX), h = static_cast<int>(gm.gmBlackBoxY);
    const int pitch = (w + 3) & ~3;
    if (static_cast<size_t>(pitch) * static_cast<size_t>(h) > raw.size()) return false;
    out.w = w;
    out.h = h;
    out.left = gm.gmptGlyphOrigin.x;
    out.top = gm.gmptGlyphOrigin.y;
    out.alpha.resize(static_cast<size_t>(w) * static_cast<size_t>(h));
    for (int y = 0; y < h; ++y) {
        for (int x = 0; x < w; ++x) {
            const int level = std::min<int>(raw[static_cast<size_t>(y) * static_cast<size_t>(pitch) + static_cast<size_t>(x)], 64);   // 0..64
            out.alpha[static_cast<size_t>(y) * static_cast<size_t>(w) + static_cast<size_t>(x)] = static_cast<uint8_t>((level * 255 + 32) / 64);
        }
    }
    return true;
}

} // namespace hud
