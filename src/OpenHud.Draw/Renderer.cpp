#include "Renderer.h"

#include <algorithm>
#include <cmath>

namespace hud {

namespace {

constexpr size_t kBatchVertices = 60000;
constexpr size_t kBatchIndices = 180000;
constexpr int kAtlasTexture = 1024;
constexpr int kMaxGlyphPixels = 400;
constexpr size_t kMaxBlankInfos = 8192;
constexpr size_t kMaxBakedVertices = 600000;   // a frame larger than this is tessellated again each time rather than kept (about 12 MB)

Vtx Lerp(const Vtx& a, const Vtx& b, float t) {
    Vtx r;
    r.x = a.x + (b.x - a.x) * t;
    r.y = a.y + (b.y - a.y) * t;
    r.u = a.u + (b.u - a.u) * t;
    r.v = a.v + (b.v - a.v) * t;
    uint32_t c = 0;
    for (int shift = 0; shift < 32; shift += 8) {
        const float ca = static_cast<float>((a.color >> shift) & 0xFF), cb = static_cast<float>((b.color >> shift) & 0xFF);
        const float v = std::clamp(ca + (cb - ca) * t, 0.0f, 255.0f);
        c |= static_cast<uint32_t>(v + 0.5f) << shift;
    }
    r.color = c;
    return r;
}

float Cross2(Pt a, Pt b, Pt p) { return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x); }

// Does the convex polygon touch the disc around (cx,cy)? An edge within the radius, or the centre inside.
bool PolygonTouchesDisc(const std::vector<Vtx>& poly, float cx, float cy, float r2) {
    const size_t n = poly.size();
    bool positive = false, negative = false;
    for (size_t i = 0; i < n; ++i) {
        const Vtx& p = poly[i];
        const Vtx& q = poly[(i + 1) % n];
        const float ex = q.x - p.x, ey = q.y - p.y, len2 = ex * ex + ey * ey;
        float t = len2 > 0 ? ((cx - p.x) * ex + (cy - p.y) * ey) / len2 : 0.0f;
        t = std::clamp(t, 0.0f, 1.0f);
        const float dx = p.x + t * ex - cx, dy = p.y + t * ey - cy;
        if (dx * dx + dy * dy <= r2) return true;
        const float side = ex * (cy - p.y) - ey * (cx - p.x);
        if (side > 0) positive = true;
        else if (side < 0) negative = true;
    }
    return !(positive && negative);
}

uint64_t GlyphKey(FontRole role, int px, uint32_t cp) {
    return (static_cast<uint64_t>(role) << 56) | (static_cast<uint64_t>(px) << 32) | cp;
}

struct FrameGuard {
    IBackend& backend;
    ~FrameGuard() { backend.EndFrame(); }
};

} // namespace

std::vector<uint32_t> DecodeUtf8(const std::string& text) {
    std::vector<uint32_t> out;
    out.reserve(text.size());
    size_t i = 0;
    while (i < text.size()) {
        const unsigned char c = static_cast<unsigned char>(text[i]);
        size_t length = 1;
        uint32_t cp = c;
        if (c >= 0xC2 && c <= 0xDF) { length = 2; cp = c & 0x1Fu; }
        else if (c >= 0xE0 && c <= 0xEF) { length = 3; cp = c & 0x0Fu; }
        else if (c >= 0xF0 && c <= 0xF4) { length = 4; cp = c & 0x07u; }
        else if (c >= 0x80) { out.push_back(0xFFFD); ++i; continue; }
        bool valid = i + length <= text.size();
        for (size_t k = 1; valid && k < length; ++k) {
            const unsigned char b = static_cast<unsigned char>(text[i + k]);
            if ((b & 0xC0) != 0x80) valid = false;
            else cp = (cp << 6) | (b & 0x3Fu);
        }
        if (!valid) { out.push_back(0xFFFD); ++i; continue; }
        out.push_back(cp);
        i += length;
    }
    return out;
}

Renderer::Renderer(IGlyphSource& glyphs) : glyphs_(glyphs) {
    atlases_.resize(2);
}

void Renderer::SetScene(std::shared_ptr<const Scene> scene) {
    std::lock_guard<std::mutex> guard(lock_);
    scene_ = std::move(scene);
}

std::shared_ptr<const Scene> Renderer::CurrentScene() const {
    std::lock_guard<std::mutex> guard(lock_);
    return scene_;
}

ImageResult Renderer::PutImage(const std::string& id, int w, int h, std::vector<uint8_t>&& rgba) {
    if (id.empty() || id.size() > kMaxImageIdBytes || w < 1 || h < 1) return ImageResult::Invalid;
    const size_t bytes = static_cast<size_t>(w) * static_cast<size_t>(h) * 4;
    if (w > 16384 || h > 16384 || bytes != rgba.size()) return ImageResult::Invalid;
    if (bytes > kMaxImageBytes) return ImageResult::TooBig;
    std::lock_guard<std::mutex> guard(lock_);
    const auto existing = images_.find(id);
    if (existing == images_.end() && images_.size() >= kMaxImages) return ImageResult::TooMany;
    const size_t previous = existing == images_.end() ? 0 : existing->second.rgba->size();
    if (imageBytes_ - previous + bytes > kMaxTotalImageBytes) return ImageResult::OutOfMemoryBudget;
    ImageData data;
    data.w = w;
    data.h = h;
    data.rgba = std::make_shared<const std::vector<uint8_t>>(std::move(rgba));
    data.version = ++imageVersion_;
    imageBytes_ = imageBytes_ - previous + bytes;
    images_[id] = std::move(data);
    return ImageResult::Ok;
}

bool Renderer::DropImage(const std::string& id) {
    std::lock_guard<std::mutex> guard(lock_);
    const auto it = images_.find(id);
    if (it == images_.end()) return false;
    imageBytes_ -= it->second.rgba->size();
    images_.erase(it);
    ++imageVersion_;
    return true;
}

int Renderer::ImageCount() const {
    std::lock_guard<std::mutex> guard(lock_);
    return static_cast<int>(images_.size());
}

RenderStats Renderer::Stats() const {
    std::lock_guard<std::mutex> guard(lock_);
    return stats_;
}

int Renderer::GlyphAtlasEvictions() const {
    int n = 0;
    for (const auto& a : atlases_) {
        if (a) n += static_cast<int>(a->lru.Evictions());
    }
    return n;
}

int Renderer::GlyphsResident() const {
    int n = 0;
    for (const auto& a : atlases_) {
        if (a) n += a->lru.Count();
    }
    return n;
}

void Renderer::InvalidateFrameCache() {
    baked_.reset();
    building_.reset();
}

void Renderer::ForgetBackendResources() {
    InvalidateFrameCache();
    backendImages_.clear();
    for (auto& a : atlases_) a.reset();
    glyphInfo_.clear();
    batchV_.clear();
    batchI_.clear();
    batchTexture_ = 0;
    backend_ = nullptr;
    imagesSynced_ = ~0ull;
}

void Renderer::ReleaseBackendResources(IBackend& backend) {
    for (auto& entry : backendImages_) backend.DestroyTexture(entry.second.tex.id);
    for (auto& a : atlases_) {
        if (a && a->tex.id != 0) backend.DestroyTexture(a->tex.id);
    }
    ForgetBackendResources();
}

void Renderer::SyncImages() {
    std::map<std::string, ImageData> wanted;
    {
        std::lock_guard<std::mutex> guard(lock_);
        if (imagesSynced_ == imageVersion_) return;
        wanted = images_;
        imagesSynced_ = imageVersion_;
    }
    for (auto it = backendImages_.begin(); it != backendImages_.end();) {
        const auto w = wanted.find(it->first);
        if (w == wanted.end() || w->second.version != it->second.version) {
            backend_->DestroyTexture(it->second.tex.id);
            it = backendImages_.erase(it);
        } else {
            ++it;
        }
    }
    for (const auto& entry : wanted) {
        if (backendImages_.count(entry.first) != 0) continue;
        BackendImage image;
        image.version = entry.second.version;
        if (backend_->CreateTexture(entry.second.w, entry.second.h, entry.second.rgba->data(), image.tex))
            backendImages_[entry.first] = image;
    }
}

void Renderer::Flush() {
    if (batchI_.empty()) return;
    if (building_) {
        BakedBatch batch;
        batch.v = batchV_;
        batch.i = batchI_;
        batch.texture = batchTexture_;
        building_->vertices += batch.v.size();
        building_->triangles += batch.i.size() / 3;
        building_->batches.push_back(std::move(batch));
    }
    if (!backend_->Draw(batchV_.data(), static_cast<int>(batchV_.size()), batchI_.data(), static_cast<int>(batchI_.size()), batchTexture_))
        failed_ = true;
    frame_.triangles += batchI_.size() / 3;
    ++frame_.drawCalls;
    batchV_.clear();
    batchI_.clear();
}

void Renderer::AppendTriangle(const Vtx& a, const Vtx& b, const Vtx& c, int texture) {
    if (texture != batchTexture_) {
        Flush();
        batchTexture_ = texture;
    }
    if (batchV_.size() + 3 > kBatchVertices || batchI_.size() + 3 > kBatchIndices) Flush();
    const uint16_t base = static_cast<uint16_t>(batchV_.size());
    batchV_.push_back(a);
    batchV_.push_back(b);
    batchV_.push_back(c);
    batchI_.push_back(base);
    batchI_.push_back(static_cast<uint16_t>(base + 1));
    batchI_.push_back(static_cast<uint16_t>(base + 2));
}

void Renderer::Submit(const Mesh& mesh, int texture) {
    if (mesh.idx.empty()) return;
    if (!clips_.empty()) {
        SubmitClipped(mesh, texture);
        return;
    }
    if (mesh.v.size() > kBatchVertices || mesh.idx.size() > kBatchIndices) {
        for (size_t i = 0; i + 2 < mesh.idx.size(); i += 3)
            AppendTriangle(mesh.v[mesh.idx[i]], mesh.v[mesh.idx[i + 1]], mesh.v[mesh.idx[i + 2]], texture);
        return;
    }
    if (texture != batchTexture_) {
        Flush();
        batchTexture_ = texture;
    }
    if (batchV_.size() + mesh.v.size() > kBatchVertices || batchI_.size() + mesh.idx.size() > kBatchIndices) Flush();
    const uint32_t base = static_cast<uint32_t>(batchV_.size());
    batchV_.insert(batchV_.end(), mesh.v.begin(), mesh.v.end());
    for (const uint32_t i : mesh.idx) batchI_.push_back(static_cast<uint16_t>(base + i));
}

bool Renderer::ClipPolygon(std::vector<Vtx>& poly) const {
    std::vector<Vtx>& next = clipNext_;
    for (const ClipPoly& clip : clips_) {
        if (poly.size() < 3 || clip.pts.size() < 3) { poly.clear(); return false; }
        float minx = poly[0].x, maxx = poly[0].x, miny = poly[0].y, maxy = poly[0].y;
        for (const Vtx& v : poly) {
            minx = std::min(minx, v.x); maxx = std::max(maxx, v.x);
            miny = std::min(miny, v.y); maxy = std::max(maxy, v.y);
        }
        if (maxx < clip.minx || minx > clip.maxx || maxy < clip.miny || miny > clip.maxy) { poly.clear(); return false; }
        if (clip.axisRect && minx >= clip.minx && maxx <= clip.maxx && miny >= clip.miny && maxy <= clip.maxy) continue;
        if (clip.circle && circleShortcuts_) {
            // Inside the inscribed disc: inside the clip, nothing to cut. Missing the circumscribed disc: outside, nothing to keep.
            bool inside = true, touching = false;
            for (const Vtx& v : poly) {
                const float dx = v.x - clip.ccx, dy = v.y - clip.ccy, d2 = dx * dx + dy * dy;
                if (d2 > clip.rIn2) inside = false;
                if (d2 <= clip.rOut2) touching = true;
            }
            if (inside) continue;
            if (!touching && !PolygonTouchesDisc(poly, clip.ccx, clip.ccy, clip.rOut2)) { poly.clear(); return false; }
        }
        // Sutherland-Hodgman, an edge at a time; an edge that every vertex is inside of cuts nothing and is skipped.
        const size_t edges = clip.pts.size();
        for (size_t e = 0; e < edges && poly.size() >= 3; ++e) {
            const Pt a = clip.pts[e], b = clip.pts[(e + 1) % edges];
            bool anyOut = false, anyIn = false;
            for (const Vtx& v : poly) {
                if (Cross2(a, b, {v.x, v.y}) < 0) anyOut = true;
                else anyIn = true;
            }
            if (!anyOut) continue;
            if (!anyIn) { poly.clear(); return false; }
            next.clear();
            for (size_t i = 0; i < poly.size(); ++i) {
                const Vtx& s = poly[i];
                const Vtx& t = poly[(i + 1) % poly.size()];
                const float ds = Cross2(a, b, {s.x, s.y}), dt = Cross2(a, b, {t.x, t.y});
                const bool sIn = ds >= 0, tIn = dt >= 0;
                if (sIn) next.push_back(s);
                if (sIn != tIn) next.push_back(Lerp(s, t, ds / (ds - dt)));
            }
            poly.swap(next);
        }
        if (poly.size() < 3) { poly.clear(); return false; }
    }
    return true;
}

void Renderer::SubmitClipped(const Mesh& mesh, int texture) {
    std::vector<Vtx> poly;
    poly.reserve(16);
    for (size_t i = 0; i + 2 < mesh.idx.size(); i += 3) {
        poly.assign({mesh.v[mesh.idx[i]], mesh.v[mesh.idx[i + 1]], mesh.v[mesh.idx[i + 2]]});
        if (!ClipPolygon(poly)) continue;
        for (size_t k = 1; k + 1 < poly.size(); ++k) AppendTriangle(poly[0], poly[k], poly[k + 1], texture);
    }
}

void Renderer::PushClip(const Cmd& cmd) {
    ClipPoly clip;
    if (cmd.clipCircle) {
        BuildCircle(clip.pts, cmd.cx, cmd.cy, cmd.r, current_);
    } else {
        clip.pts = {current_.Apply({cmd.x, cmd.y}), current_.Apply({cmd.x + cmd.w, cmd.y}),
                    current_.Apply({cmd.x + cmd.w, cmd.y + cmd.h}), current_.Apply({cmd.x, cmd.y + cmd.h})};
        clip.axisRect = current_.AxisAligned();
    }
    double area = 0;
    for (size_t i = 0; i < clip.pts.size(); ++i) {
        const Pt p = clip.pts[i], q = clip.pts[(i + 1) % clip.pts.size()];
        area += static_cast<double>(p.x) * q.y - static_cast<double>(q.x) * p.y;
    }
    if (std::fabs(area) < 1e-6) {
        clip.pts.clear();   // an empty clip rejects everything
    } else if (area < 0) {
        std::reverse(clip.pts.begin(), clip.pts.end());
    }
    if (!clip.pts.empty()) {
        clip.minx = clip.maxx = clip.pts[0].x;
        clip.miny = clip.maxy = clip.pts[0].y;
        for (const Pt& p : clip.pts) {
            clip.minx = std::min(clip.minx, p.x); clip.maxx = std::max(clip.maxx, p.x);
            clip.miny = std::min(clip.miny, p.y); clip.maxy = std::max(clip.maxy, p.y);
        }
    }
    if (cmd.clipCircle && !clip.pts.empty()) {
        const float columnA = current_.a * current_.a + current_.b * current_.b;
        const float columnB = current_.c * current_.c + current_.d * current_.d;
        const float tolerance = 1e-4f * std::max(1.0f, columnA);
        if (std::fabs(columnA - columnB) <= tolerance && std::fabs(current_.a * current_.c + current_.b * current_.d) <= tolerance) {
            const Pt centre = current_.Apply({cmd.cx, cmd.cy});
            const float radius = cmd.r * std::sqrt(columnA);
            const float n = static_cast<float>(clip.pts.size());
            clip.circle = true;
            clip.ccx = centre.x;
            clip.ccy = centre.y;
            const float outer = radius * 1.002f + 0.01f;
            const float inner = std::max(0.0f, radius * std::cos(3.14159265f / n) * 0.998f - 0.01f);
            clip.rOut2 = outer * outer;
            clip.rIn2 = inner * inner;
        }
    }
    clips_.push_back(std::move(clip));
}

void Renderer::DrawShape(const std::vector<Pt>& ring, bool closed, const Cmd& cmd, float strokeWidth) {
    if (cmd.hasFill) {
        mesh_.Clear();
        AddFill(mesh_, ring, cmd.fill.Argb());
        Submit(mesh_, 0);
    }
    if (cmd.hasStroke && cmd.sw > 0) {
        mesh_.Clear();
        AddStroke(mesh_, ring, closed, strokeWidth, cmd.stroke.Argb(), Cap::Butt);
        Submit(mesh_, 0);
    }
}

void Renderer::DrawCommand(const Cmd& cmd) {
    const float sc = current_.Scale();
    std::vector<Pt> ring;
    switch (cmd.kind) {
    case CmdKind::Rect:
        if (cmd.w <= 0 || cmd.h <= 0) break;
        BuildRoundRect(ring, cmd.x, cmd.y, cmd.w, cmd.h, cmd.r, current_);
        DrawShape(ring, true, cmd, cmd.sw * sc);
        break;
    case CmdKind::Circle:
        if (cmd.r <= 0) break;
        BuildCircle(ring, cmd.cx, cmd.cy, cmd.r, current_);
        DrawShape(ring, true, cmd, cmd.sw * sc);
        break;
    case CmdKind::Arc: {
        if (cmd.r <= 0) break;
        const bool full = BuildArc(ring, cmd.cx, cmd.cy, cmd.r, cmd.a0, cmd.a1, current_);
        mesh_.Clear();
        AddStroke(mesh_, ring, full, cmd.sw * sc, cmd.stroke.Argb(), cmd.cap);
        Submit(mesh_, 0);
        break;
    }
    case CmdKind::Line:
        ring = {current_.Apply({cmd.x, cmd.y}), current_.Apply({cmd.x2, cmd.y2})};
        mesh_.Clear();
        AddStroke(mesh_, ring, false, cmd.sw * sc, cmd.stroke.Argb(), cmd.cap);
        Submit(mesh_, 0);
        break;
    case CmdKind::Poly:
        for (size_t i = 0; i + 1 < cmd.pts.size(); i += 2) ring.push_back(current_.Apply({cmd.pts[i], cmd.pts[i + 1]}));
        DrawShape(ring, cmd.closed, cmd, cmd.sw * sc);
        break;
    case CmdKind::Text:
        DrawText(cmd);
        break;
    case CmdKind::Image:
        DrawImage(cmd);
        break;
    case CmdKind::Clip:
        PushClip(cmd);
        break;
    case CmdKind::Unclip:
        if (!clips_.empty()) clips_.pop_back();
        break;
    case CmdKind::Push: {
        stack_.push_back(current_);
        current_ = current_.Times(Mat::Translate(cmd.tx, cmd.ty)).Times(Mat::Rotate(cmd.rot)).Times(Mat::ScaleBy(cmd.sx, cmd.sy));
        break;
    }
    case CmdKind::Pop:
        if (!stack_.empty()) {
            current_ = stack_.back();
            stack_.pop_back();
        }
        break;
    }
}

void Renderer::DrawImage(const Cmd& cmd) {
    const auto it = backendImages_.find(cmd.text);
    if (it == backendImages_.end()) {
        ++frame_.missingImages;
        return;
    }
    const TextureRef& tex = it->second.tex;
    Mat m = current_;
    if (cmd.rot != 0.0f) {
        const float cx = cmd.x + cmd.w * 0.5f, cy = cmd.y + cmd.h * 0.5f;
        m = m.Times(Mat::Translate(cx, cy)).Times(Mat::Rotate(cmd.rot)).Times(Mat::Translate(-cx, -cy));
    }
    uint32_t color = 0x00FFFFFFu;
    float alpha = cmd.op;
    if (cmd.hasTint) {
        color = cmd.tint.Argb() & 0x00FFFFFFu;
        alpha *= static_cast<float>(cmd.tint.a) / 255.0f;
    }
    color |= static_cast<uint32_t>(std::clamp(alpha, 0.0f, 1.0f) * 255.0f + 0.5f) << 24;
    const Pt p0 = m.Apply({cmd.x, cmd.y}), p1 = m.Apply({cmd.x + cmd.w, cmd.y});
    const Pt p2 = m.Apply({cmd.x + cmd.w, cmd.y + cmd.h}), p3 = m.Apply({cmd.x, cmd.y + cmd.h});
    mesh_.Clear();
    const uint32_t a = mesh_.Add(p0.x, p0.y, color, 0, 0);
    const uint32_t b = mesh_.Add(p1.x, p1.y, color, tex.u1, 0);
    const uint32_t c = mesh_.Add(p2.x, p2.y, color, tex.u1, tex.v1);
    const uint32_t d = mesh_.Add(p3.x, p3.y, color, 0, tex.v1);
    mesh_.Quad(a, b, c, d);
    Submit(mesh_, tex.id);
}

Renderer::Atlas* Renderer::AtlasFor(int index) {
    std::unique_ptr<Atlas>& slot = atlases_[static_cast<size_t>(index)];
    if (!slot) {
        auto atlas = std::make_unique<Atlas>(index == 0 ? 32 : 128);
        if (!backend_->CreateTexture(kAtlasTexture, kAtlasTexture, nullptr, atlas->tex)) return nullptr;
        slot = std::move(atlas);
    }
    return slot.get();
}

const FaceMetrics* Renderer::MetricsFor(FontRole role, int px) {
    const uint64_t key = (static_cast<uint64_t>(role) << 32) | static_cast<uint32_t>(px);
    const auto it = metrics_.find(key);
    if (it != metrics_.end()) return &it->second;
    FaceMetrics m;
    if (!glyphs_.Metrics(role, px, m)) {
        m.ascent = static_cast<float>(px) * 0.8f;
        m.descent = static_cast<float>(px) * 0.2f;
    }
    return &(metrics_[key] = m);
}

const Renderer::GlyphInfo* Renderer::Glyph(FontRole role, int px, uint32_t cp) {
    const uint32_t generation = glyphs_.Generation();
    if (generation != fontGeneration_) {   // the registered fonts changed: every cached glyph is stale
        Flush();
        glyphInfo_.clear();
        for (auto& a : atlases_) {
            if (a) a->lru.Clear();
        }
        metrics_.clear();
        fontGeneration_ = generation;
    }
    const uint64_t key = GlyphKey(role, px, cp);
    const auto found = glyphInfo_.find(key);
    if (found != glyphInfo_.end()) {
        if (found->second.atlas >= 0) atlases_[static_cast<size_t>(found->second.atlas)]->lru.Find(key);
        return &found->second;
    }
    GlyphInfo info;
    GlyphBitmap bitmap;
    if (!glyphs_.Glyph(role, px, cp, bitmap)) {
        ++frame_.glyphFailures;
        info.advance = static_cast<float>(px) * 0.5f;
    } else {
        info.advance = bitmap.advance;
        info.w = bitmap.w; info.h = bitmap.h; info.left = bitmap.left; info.top = bitmap.top;
        if (bitmap.w > 0) {
            const int cellNeed = std::max(bitmap.w, bitmap.h) + 2;   // one transparent texel around the glyph
            const int which = cellNeed <= 32 ? 0 : (cellNeed <= 128 ? 1 : -1);
            Atlas* atlas = which < 0 ? nullptr : AtlasFor(which);
            if (atlas == nullptr) {
                ++frame_.glyphFailures;
            } else {
                bool evicted = false;
                uint64_t evictedKey = 0;
                const int cell = atlas->lru.Acquire(key, evicted, evictedKey);
                if (evicted) {
                    Flush();   // quads still queued may sample the cell that is about to be reused
                    glyphInfo_.erase(evictedKey);
                    ++frame_.atlasEvictions;
                }
                const int w = bitmap.w + 2, h = bitmap.h + 2;
                std::vector<uint8_t> rgba(static_cast<size_t>(w) * static_cast<size_t>(h) * 4, 0);
                for (size_t i = 0; i < rgba.size(); i += 4) rgba[i] = rgba[i + 1] = rgba[i + 2] = 255;
                for (int y = 0; y < bitmap.h; ++y) {
                    for (int x = 0; x < bitmap.w; ++x) {
                        rgba[(static_cast<size_t>(y + 1) * static_cast<size_t>(w) + static_cast<size_t>(x + 1)) * 4 + 3] =
                            bitmap.alpha[static_cast<size_t>(y) * static_cast<size_t>(bitmap.w) + static_cast<size_t>(x)];
                    }
                }
                backend_->UpdateTexture(atlas->tex.id, atlas->lru.CellX(cell), atlas->lru.CellY(cell), w, h, rgba.data(), w * 4);
                info.atlas = which;
                info.cell = cell;
            }
        }
    }
    if (info.atlas < 0 && glyphInfo_.size() > kMaxBlankInfos) {
        for (auto it = glyphInfo_.begin(); it != glyphInfo_.end();) {
            if (it->second.atlas < 0) it = glyphInfo_.erase(it);
            else ++it;
        }
    }
    return &(glyphInfo_[key] = info);
}

void Renderer::DrawText(const Cmd& cmd) {
    const float sc = current_.Scale();
    if (sc <= 1e-6f) return;
    const int px = std::clamp(static_cast<int>(std::lround(cmd.size * sc)), 1, kMaxGlyphPixels);
    std::vector<uint32_t> cps = DecodeUtf8(cmd.text);
    if (cps.empty()) return;

    std::vector<float> advances(cps.size());
    float total = 0;
    for (size_t i = 0; i < cps.size(); ++i) {
        advances[i] = Glyph(cmd.font, px, cps[i])->advance;
        total += advances[i];
    }
    if (cmd.maxw > 0 && total > cmd.maxw * sc) {
        const float limit = cmd.maxw * sc;
        const float dot = Glyph(cmd.font, px, '.')->advance;
        float width = 0;
        size_t keep = 0;
        while (keep < cps.size() && width + advances[keep] + dot * 3 <= limit) width += advances[keep++];
        cps.resize(keep);
        advances.resize(keep);
        for (int i = 0; i < 3; ++i) {
            if (width + dot > limit) break;
            cps.push_back('.');
            advances.push_back(dot);
            width += dot;
        }
        total = width;
        if (cps.empty()) return;
    }

    const FaceMetrics& fm = *MetricsFor(cmd.font, px);
    const float totalLocal = total / sc;
    float originX = cmd.x;
    if (cmd.align == Align::Center) originX -= totalLocal * 0.5f;
    else if (cmd.align == Align::Right) originX -= totalLocal;
    float baseline = cmd.y;
    if (cmd.base == Base::Top) baseline += fm.ascent / sc;
    else if (cmd.base == Base::Mid) baseline += (fm.ascent - fm.descent) * 0.5f / sc;

    const bool snap = current_.a > 0 && current_.d > 0 && current_.AxisAligned();
    const auto pass = [&](uint32_t color, float dx, float dy) {
        float pen = 0;
        for (size_t i = 0; i < cps.size(); ++i) {
            const float advance = advances[i];
            const GlyphInfo info = *Glyph(cmd.font, px, cps[i]);   // by value: a later fetch may evict its entry
            if (info.atlas >= 0) {
                Atlas& atlas = *atlases_[static_cast<size_t>(info.atlas)];
                const float tx = static_cast<float>(atlas.lru.CellX(info.cell) + 1), ty = static_cast<float>(atlas.lru.CellY(info.cell) + 1);
                const float u0 = tx / kAtlasTexture * atlas.tex.u1, u1 = (tx + static_cast<float>(info.w)) / kAtlasTexture * atlas.tex.u1;
                const float v0 = ty / kAtlasTexture * atlas.tex.v1, v1 = (ty + static_cast<float>(info.h)) / kAtlasTexture * atlas.tex.v1;
                Pt c0, c1, c2, c3;
                const float penLocal = originX + pen / sc;
                if (snap) {
                    const Pt origin = current_.Apply({penLocal, baseline});
                    const float x = std::round(origin.x + static_cast<float>(info.left) + dx);
                    const float y = std::round(origin.y - static_cast<float>(info.top) + dy);
                    c0 = {x, y};
                    c1 = {x + static_cast<float>(info.w), y};
                    c2 = {x + static_cast<float>(info.w), y + static_cast<float>(info.h)};
                    c3 = {x, y + static_cast<float>(info.h)};
                } else {
                    const float lx = penLocal + (static_cast<float>(info.left) + dx) / sc;
                    const float ly = baseline + (-static_cast<float>(info.top) + dy) / sc;
                    const float lw = static_cast<float>(info.w) / sc, lh = static_cast<float>(info.h) / sc;
                    c0 = current_.Apply({lx, ly});
                    c1 = current_.Apply({lx + lw, ly});
                    c2 = current_.Apply({lx + lw, ly + lh});
                    c3 = current_.Apply({lx, ly + lh});
                }
                mesh_.Clear();
                const uint32_t a = mesh_.Add(c0.x, c0.y, color, u0, v0);
                const uint32_t b = mesh_.Add(c1.x, c1.y, color, u1, v0);
                const uint32_t c = mesh_.Add(c2.x, c2.y, color, u1, v1);
                const uint32_t d = mesh_.Add(c3.x, c3.y, color, u0, v1);
                mesh_.Quad(a, b, c, d);
                Submit(mesh_, atlas.tex.id);
                ++frame_.glyphsDrawn;
            }
            pen += advance;
        }
    };
    if (cmd.hasOutline && cmd.outline.a != 0) {
        for (int oy = -1; oy <= 1; ++oy) {
            for (int ox = -1; ox <= 1; ++ox) {
                if (ox != 0 || oy != 0) pass(cmd.outline.Argb(), static_cast<float>(ox), static_cast<float>(oy));
            }
        }
    }
    pass(cmd.fill.Argb(), 0, 0);
}

bool Renderer::CanReplay(const std::shared_ptr<const Scene>& scene, const IBackend& backend, int viewW, int viewH, uint32_t fonts, uint64_t images) const {
    const BakedFrame* b = baked_.get();
    return b != nullptr && b->scene.get() == scene.get() && b->backend == &backend && b->viewW == viewW && b->viewH == viewH &&
           b->fonts == fonts && b->images == images;
}

bool Renderer::Replay(IBackend& backend) {
    bool ok = true;
    for (const BakedBatch& batch : baked_->batches) {
        if (!backend.Draw(batch.v.data(), static_cast<int>(batch.v.size()), batch.i.data(), static_cast<int>(batch.i.size()), batch.texture)) ok = false;
        ++frame_.drawCalls;
    }
    frame_.triangles = baked_->triangles;
    frame_.commandsDrawn = baked_->scene->cmds.size();
    ++frame_.replays;
    return ok;
}

RenderResult Renderer::Render(IBackend& backend) {
    const std::shared_ptr<const Scene> scene = CurrentScene();
    if (!scene || (scene->cmds.empty() && !scene->background)) return RenderResult::Nothing;
    if (backend_ != nullptr && backend_ != &backend) ForgetBackendResources();
    backend_ = &backend;
    int viewW = 0, viewH = 0;
    if (!backend.BeginFrame(viewW, viewH)) return RenderResult::Skipped;
    FrameGuard guard{backend};
    if (viewW < 1 || viewH < 1) return RenderResult::Skipped;

    frame_ = RenderStats();
    failed_ = false;
    stack_.clear();
    clips_.clear();
    batchV_.clear();
    batchI_.clear();
    batchTexture_ = 0;
    uint64_t imagesNow = 0;
    {
        std::lock_guard<std::mutex> lockGuard(lock_);
        imagesNow = imageVersion_;
    }
    const uint32_t fontsNow = glyphs_.Generation();
    SyncImages();

    if (scene->background && backend.HasBackground()) backend.DrawBackground();

    if (CanReplay(scene, backend, viewW, viewH, fontsNow, imagesNow)) {
        if (!Replay(backend)) failed_ = true;
        if (failed_) InvalidateFrameCache();
    } else {
        // Tessellate: interpret the commands, recording each batch as it goes to the backend.
        baked_.reset();
        building_ = std::make_unique<BakedFrame>();
        building_->scene = scene;
        building_->backend = &backend;
        building_->viewW = viewW;
        building_->viewH = viewH;
        building_->fonts = fontsNow;
        building_->images = imagesNow;
        ++frame_.tessellations;

        const float s = std::min(static_cast<float>(viewW) / static_cast<float>(scene->w), static_cast<float>(viewH) / static_cast<float>(scene->h));
        current_ = Mat::Translate((static_cast<float>(viewW) - static_cast<float>(scene->w) * s) * 0.5f,
                                  (static_cast<float>(viewH) - static_cast<float>(scene->h) * s) * 0.5f)
                       .Times(Mat::ScaleBy(s, s));
        for (const Cmd& cmd : scene->cmds) {
            DrawCommand(cmd);
            ++frame_.commandsDrawn;
        }
        Flush();

        // Kept only when the frame is complete and everything it points to is still what it was: no failure, no missing
        // picture (it may arrive), no glyph that did not fit, no cell evicted while drawing, no font or image change on the way.
        uint64_t imagesAfter = 0;
        {
            std::lock_guard<std::mutex> lockGuard(lock_);
            imagesAfter = imageVersion_;
        }
        const bool keep = !failed_ && frame_.missingImages == 0 && frame_.glyphFailures == 0 && frame_.atlasEvictions == 0 &&
                          glyphs_.Generation() == fontsNow && imagesAfter == imagesNow && building_->vertices <= kMaxBakedVertices;
        if (keep) baked_ = std::move(building_);
        building_.reset();
    }

    {
        std::lock_guard<std::mutex> lockGuard(lock_);
        ++stats_.framesDrawn;
        stats_.commandsDrawn += frame_.commandsDrawn;
        stats_.unknownCommands += static_cast<uint64_t>(scene->unknownCommands);
        stats_.skippedCommands += static_cast<uint64_t>(scene->skippedCommands);
        stats_.missingImages += frame_.missingImages;
        stats_.glyphsDrawn += frame_.glyphsDrawn;
        stats_.glyphFailures += frame_.glyphFailures;
        stats_.atlasEvictions += frame_.atlasEvictions;
        stats_.triangles += frame_.triangles;
        stats_.drawCalls += frame_.drawCalls;
        stats_.tessellations += frame_.tessellations;
        stats_.replays += frame_.replays;
    }
    return failed_ ? RenderResult::Failed : RenderResult::Drawn;
}

} // namespace hud
