#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_FULL_WALKS_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_FULL_WALKS_H_

#include <cstddef>
#include <cstdint>
#include <string>
#include <unordered_map>

// When a document is walked in full (protocol 0.35). The change records of a
// document apply to the walk before them, so a document is walked when it
// has no walk yet, when a record of its channel was lost since its last walk,
// and at every Nth request when the recording checks its change records. It
// uses the C++ standard library alone, so it can be exercised outside a
// Chromium build, as the layout change filter is. It is not thread-safe; the
// bridge holds its lock around each call.
namespace a11y_recorder {

// A source that names a layout change set instead of a layout checkpoint,
// where the bridge's presentation and interaction functions take one
// sequence. Checkpoint and change set sequences start at 1 and are counted
// apart, so the top bit is free in both.
inline constexpr uint64_t kLayoutChangeSetSourceBit = uint64_t{1} << 63;

inline uint64_t LayoutChangeSetSource(uint64_t change_set_sequence) {
  return change_set_sequence == 0
             ? 0
             : (change_set_sequence | kLayoutChangeSetSourceBit);
}

class FullWalkSchedule {
 public:
  // An interval of 0 walks no document to check its change records.
  explicit FullWalkSchedule(int interval);

  // Why a checkpoint Blink requests is walked, or an empty string when it is
  // not: "first" when the document has no walk yet, "after-loss" when a
  // record of the checkpoint's channel was lost since the document's last
  // walk, "check" at every Nth request when the recording checks its change
  // records, and, for a DOM checkpoint requested as "finished-parsing", which
  // is always walked, "finished-parsing" otherwise. `losses` is the count of
  // lost records of the channel so far.
  //
  // A DOM checkpoint is requested as "finished-parsing", or as
  // "post-mutation" at each mutation delivery. A layout checkpoint is
  // requested at each rendering update in which the document's style or
  // layout was recalculated.
  std::string DomWalkReason(int document_node_id,
                            const std::string& requested,
                            uint64_t losses);
  std::string LayoutWalkReason(int document_node_id, uint64_t losses);

  // How many documents' state is held for each kind of walk. A document
  // beyond it that is forgotten is walked at its next request.
  static constexpr size_t kMaximumDocuments = 4096;

 private:
  struct Document {
    bool walked = false;
    uint64_t losses_at_walk = 0;
    int64_t requests_since_walk = 0;
    uint64_t last_use = 0;
  };
  Document& Find(std::unordered_map<int, Document>& documents,
                 int document_node_id);
  std::string Decide(Document& document, uint64_t losses, bool always);

  int interval_;
  uint64_t use_clock_ = 0;
  std::unordered_map<int, Document> dom_documents_;
  std::unordered_map<int, Document> layout_documents_;
};

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_FULL_WALKS_H_
