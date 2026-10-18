#pragma once
// Fonts: registration of the module's own font files (private to the process),
// a reader of the few sfnt fields needed to select them, and the GDI glyph source
// that rasterizes anti-aliased glyphs. No font is embedded in the DLL; a role
// without a registered file, and any character a file lacks, uses the system UI font.
#include "Scene.h"

#include <cstdint>
#include <functional>
#include <map>
#include <mutex>
#include <string>
#include <vector>

namespace hud {

struct FontFace {
    std::wstring path;
    std::wstring family;
    int weight = 400;
    bool italic = false;
};

// Reads (offset, length) bytes of a font file; false when out of range.
using ByteReader = std::function<bool(uint64_t offset, void* dst, size_t length)>;
// Extracts the family name (name id 1), the weight class and the italic flag.
bool ReadSfntInfo(const ByteReader& read, uint64_t fileSize, FontFace& face, std::string& error);

struct GlyphBitmap {
    int w = 0, h = 0;          // 0 for blank glyphs (space)
    int left = 0, top = 0;     // origin of the bitmap relative to the pen on the baseline; top is above the baseline
    float advance = 0;         // pen advance in pixels
    std::vector<uint8_t> alpha;   // w*h coverage
};

struct FaceMetrics {
    float ascent = 0, descent = 0;
};

class IGlyphSource {
public:
    virtual ~IGlyphSource() = default;
    virtual bool Metrics(FontRole role, int px, FaceMetrics& out) = 0;
    virtual bool Glyph(FontRole role, int px, uint32_t codepoint, GlyphBitmap& out) = 0;
    // Changes whenever the set of registered fonts changes (cached glyphs are then stale).
    virtual uint32_t Generation() const = 0;
};

class FontRegistry {
public:
    ~FontRegistry();
    // Validates the file, registers it privately for this process and binds it to the role.
    bool Put(FontRole role, const std::wstring& path, std::string& error);
    void Clear();
    int Count() const;
    bool Get(FontRole role, FontFace& face) const;
    uint32_t Generation() const;

private:
    mutable std::mutex lock_;
    std::map<int, FontFace> faces_;
    uint32_t generation_ = 0;
};

class GdiGlyphSource final : public IGlyphSource {
public:
    explicit GdiGlyphSource(FontRegistry& registry);
    ~GdiGlyphSource() override;
    bool Metrics(FontRole role, int px, FaceMetrics& out) override;
    bool Glyph(FontRole role, int px, uint32_t codepoint, GlyphBitmap& out) override;
    uint32_t Generation() const override { return registry_.Generation(); }

private:
    struct Impl;
    FontRegistry& registry_;
    Impl* impl_;
};

} // namespace hud
