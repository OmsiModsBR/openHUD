#include "Atlas.h"

namespace hud {

LruAtlas::LruAtlas(int cellSize, int columns, int rows) : cell_(cellSize), columns_(columns), rows_(rows) {
    nodes_.resize(static_cast<size_t>(columns) * static_cast<size_t>(rows));
    Clear();
}

void LruAtlas::Clear() {
    map_.clear();
    head_ = tail_ = -1;
    free_.clear();
    for (int i = static_cast<int>(nodes_.size()) - 1; i >= 0; --i) {
        nodes_[static_cast<size_t>(i)] = Node();
        free_.push_back(i);   // lowest cell is handed out first
    }
}

void LruAtlas::Unlink(int i) {
    Node& n = nodes_[static_cast<size_t>(i)];
    if (n.prev >= 0) nodes_[static_cast<size_t>(n.prev)].next = n.next; else head_ = n.next;
    if (n.next >= 0) nodes_[static_cast<size_t>(n.next)].prev = n.prev; else tail_ = n.prev;
    n.prev = n.next = -1;
}

void LruAtlas::PushFront(int i) {
    Node& n = nodes_[static_cast<size_t>(i)];
    n.prev = -1;
    n.next = head_;
    if (head_ >= 0) nodes_[static_cast<size_t>(head_)].prev = i;
    head_ = i;
    if (tail_ < 0) tail_ = i;
}

int LruAtlas::Find(uint64_t key) {
    const auto it = map_.find(key);
    if (it == map_.end()) return -1;
    const int i = it->second;
    if (head_ != i) {
        Unlink(i);
        PushFront(i);
    }
    return i;
}

int LruAtlas::Acquire(uint64_t key, bool& evicted, uint64_t& evictedKey) {
    evicted = false;
    int i;
    if (!free_.empty()) {
        i = free_.back();
        free_.pop_back();
    } else {
        i = tail_;
        if (i < 0) return -1;
        evicted = true;
        evictedKey = nodes_[static_cast<size_t>(i)].key;
        map_.erase(evictedKey);
        Unlink(i);
        ++evictions_;
    }
    Node& n = nodes_[static_cast<size_t>(i)];
    n.key = key;
    n.used = true;
    map_[key] = i;
    PushFront(i);
    return i;
}

} // namespace hud
