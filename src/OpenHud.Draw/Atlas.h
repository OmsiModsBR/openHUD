#pragma once
// A grid of equally sized cells with least-recently-used eviction. The glyph
// atlas uses it: a texture is cut into cells, each cell holds one glyph, and when
// every cell is taken the glyph that was drawn longest ago gives up its cell.
#include <cstdint>
#include <unordered_map>
#include <vector>

namespace hud {

class LruAtlas {
public:
    LruAtlas(int cellSize, int columns, int rows);

    int CellSize() const { return cell_; }
    int Capacity() const { return static_cast<int>(nodes_.size()); }
    int Count() const { return static_cast<int>(map_.size()); }
    uint64_t Evictions() const { return evictions_; }

    // The cell that holds `key` (marked most recently used), or -1.
    int Find(uint64_t key);
    // Takes a cell for `key` (which must not be present): a free one, otherwise the
    // least recently used, whose previous key is reported through evictedKey.
    int Acquire(uint64_t key, bool& evicted, uint64_t& evictedKey);
    void Clear();

    int CellX(int cell) const { return (cell % columns_) * cell_; }
    int CellY(int cell) const { return (cell / columns_) * cell_; }
    int TextureWidth() const { return columns_ * cell_; }
    int TextureHeight() const { return rows_ * cell_; }

private:
    struct Node {
        uint64_t key = 0;
        int prev = -1, next = -1;
        bool used = false;
    };
    int cell_, columns_, rows_;
    std::vector<Node> nodes_;
    std::unordered_map<uint64_t, int> map_;
    int head_ = -1;   // most recently used
    int tail_ = -1;   // least recently used
    std::vector<int> free_;
    uint64_t evictions_ = 0;

    void Unlink(int i);
    void PushFront(int i);
};

} // namespace hud
