#include "chromium/recorder_bridge/full_walks.h"

namespace a11y_recorder {

FullWalkSchedule::FullWalkSchedule(int interval)
    : interval_(interval < 0 ? 0 : interval) {}

FullWalkSchedule::Document& FullWalkSchedule::Find(
    std::unordered_map<int, Document>& documents,
    int document_node_id) {
  if (!documents.contains(document_node_id) &&
      documents.size() >= kMaximumDocuments) {
    auto oldest = documents.begin();
    for (auto entry = documents.begin(); entry != documents.end(); ++entry) {
      if (entry->second.last_use < oldest->second.last_use) {
        oldest = entry;
      }
    }
    documents.erase(oldest);
  }
  Document& document = documents[document_node_id];
  document.last_use = ++use_clock_;
  return document;
}

std::string FullWalkSchedule::Decide(Document& document,
                                     uint64_t losses,
                                     bool always) {
  std::string reason;
  if (!document.walked) {
    reason = "first";
  } else if (document.losses_at_walk != losses) {
    reason = "after-loss";
  } else if (interval_ > 0 && ++document.requests_since_walk >= interval_) {
    reason = "check";
  } else if (always) {
    reason = "finished-parsing";
  } else {
    return std::string();
  }
  document.walked = true;
  document.losses_at_walk = losses;
  document.requests_since_walk = 0;
  return reason;
}

std::string FullWalkSchedule::DomWalkReason(int document_node_id,
                                            const std::string& requested,
                                            uint64_t losses) {
  if (requested != "finished-parsing" && requested != "post-mutation") {
    return std::string();
  }
  return Decide(Find(dom_documents_, document_node_id), losses,
                requested == "finished-parsing");
}

std::string FullWalkSchedule::LayoutWalkReason(int document_node_id,
                                               uint64_t losses) {
  return Decide(Find(layout_documents_, document_node_id), losses, false);
}

}  // namespace a11y_recorder
