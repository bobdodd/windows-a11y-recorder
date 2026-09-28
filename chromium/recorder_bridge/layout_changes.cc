#include "chromium/recorder_bridge/layout_changes.h"

#include <cstring>

namespace a11y_recorder {

namespace {

// FNV-1a over each field's bytes. Every variable-length field is preceded by
// its length, so no two different records hash the same bytes.
class Hasher {
 public:
  void Bytes(const void* data, size_t size) {
    const unsigned char* bytes = static_cast<const unsigned char*>(data);
    for (size_t index = 0; index < size; ++index) {
      hash_ ^= bytes[index];
      hash_ *= 1099511628211ull;
    }
  }
  void Integer(uint64_t value) { Bytes(&value, sizeof(value)); }
  void Boolean(bool value) { Integer(value ? 1 : 0); }
  void Number(double value) {
    uint64_t bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));
    Integer(bits);
  }
  void Text(const std::string& value) {
    Integer(value.size());
    Bytes(value.data(), value.size());
  }
  uint64_t Value() const { return hash_; }

 private:
  uint64_t hash_ = 14695981039346656037ull;
};

}  // namespace

uint64_t HashLayoutChangedNode(const LayoutChangedNode& changed) {
  Hasher hasher;
  const LayoutCheckpointNode& node = changed.node;
  hasher.Integer(static_cast<uint64_t>(node.node_id));
  hasher.Integer(static_cast<uint64_t>(node.node_type));
  hasher.Text(node.node_name);
  hasher.Boolean(node.layout_object_present);
  hasher.Boolean(node.display_locked);
  hasher.Boolean(node.computed_style_present);
  hasher.Integer(node.computed_style.size());
  for (const LayoutCheckpointStyleValue& value : node.computed_style) {
    hasher.Text(value.property_name);
    hasher.Boolean(value.value_present);
    hasher.Text(value.value);
  }
  hasher.Boolean(node.pseudo_element_present);
  hasher.Integer(static_cast<uint64_t>(node.originating_node_id));
  hasher.Text(node.pseudo_type);
  hasher.Text(node.generated_text);
  hasher.Integer(static_cast<uint64_t>(node.generated_text_length));
  hasher.Boolean(node.generated_text_truncated);
  hasher.Integer(static_cast<uint64_t>(node.shadow_host_node_id));
  hasher.Text(node.shadow_root_mode);
  hasher.Boolean(changed.geometry_present);
  hasher.Integer(changed.transform_node_id);
  hasher.Boolean(changed.client_rect_empty);
  hasher.Boolean(changed.local_rect_mapped);
  hasher.Number(changed.local_x);
  hasher.Number(changed.local_y);
  hasher.Number(changed.local_width);
  hasher.Number(changed.local_height);
  hasher.Number(changed.client_rect_scale);
  return hasher.Value();
}

uint64_t HashLayoutTransformNode(const LayoutTransformNode& node) {
  Hasher hasher;
  hasher.Integer(node.id);
  hasher.Integer(node.parent_id);
  for (double value : node.matrix) {
    hasher.Number(value);
  }
  hasher.Boolean(node.flattens_inherited_transform);
  hasher.Boolean(node.scroll_translation);
  hasher.Boolean(node.sticky);
  return hasher.Value();
}

bool LayoutChangeFilter::NodeChanged(int node_id, uint64_t hash) {
  auto [found, inserted] = nodes_.try_emplace(node_id, hash);
  if (inserted) {
    return true;
  }
  if (found->second == hash) {
    return false;
  }
  found->second = hash;
  return true;
}

bool LayoutChangeFilter::TransformNodeChanged(uint64_t transform_node_id,
                                              uint64_t hash) {
  auto [found, inserted] = transform_nodes_.try_emplace(transform_node_id, hash);
  if (inserted) {
    return true;
  }
  if (found->second == hash) {
    return false;
  }
  found->second = hash;
  return true;
}

}  // namespace a11y_recorder
