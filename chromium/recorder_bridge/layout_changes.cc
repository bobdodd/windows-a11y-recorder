#include "chromium/recorder_bridge/layout_changes.h"

#include <algorithm>
#include <bit>
#include <utility>

namespace a11y_recorder {

namespace {

// FNV-1a over each field's bytes. Every variable-length field is preceded by
// its length, so no two different records hash the same bytes. Bytes are
// taken by value, never through a pointer and length, so the hashing holds
// no buffer access.
class Hasher {
 public:
  void Byte(unsigned char byte) {
    hash_ ^= byte;
    hash_ *= 1099511628211ull;
  }
  void Integer(uint64_t value) {
    for (int shift = 0; shift < 64; shift += 8) {
      Byte(static_cast<unsigned char>(value >> shift));
    }
  }
  void Boolean(bool value) { Integer(value ? 1 : 0); }
  void Number(double value) { Integer(std::bit_cast<uint64_t>(value)); }
  void Text(const std::string& value) {
    Integer(value.size());
    for (char character : value) {
      Byte(static_cast<unsigned char>(character));
    }
  }
  uint64_t Value() const { return hash_; }

 private:
  uint64_t hash_ = 14695981039346656037ull;
};

uint64_t HashStyleValue(const LayoutCheckpointStyleValue& value) {
  Hasher hasher;
  hasher.Text(value.property_name);
  hasher.Boolean(value.value_present);
  hasher.Text(value.value);
  return hasher.Value();
}

void HashBoxFragment(Hasher& hasher, const LayoutBoxFragment& fragment) {
  hasher.Number(fragment.width);
  hasher.Number(fragment.height);
  hasher.Boolean(fragment.break_token_present);
  hasher.Number(fragment.consumed_block_size);
  hasher.Boolean(fragment.break_before);
  hasher.Integer(fragment.sequence_number);
  hasher.Boolean(fragment.at_block_end);
  hasher.Boolean(fragment.scrollable_overflow_present);
  hasher.Number(fragment.scrollable_overflow.x);
  hasher.Number(fragment.scrollable_overflow.y);
  hasher.Number(fragment.scrollable_overflow.width);
  hasher.Number(fragment.scrollable_overflow.height);
  hasher.Integer(fragment.children.size());
  for (const LayoutFragmentChild& child : fragment.children) {
    hasher.Text(child.kind);
    hasher.Integer(static_cast<uint64_t>(child.node_id));
    hasher.Integer(static_cast<uint64_t>(child.fragment_index));
    hasher.Number(child.x);
    hasher.Number(child.y);
    hasher.Integer(child.fragment.size());
    for (const LayoutBoxFragment& nested : child.fragment) {
      HashBoxFragment(hasher, nested);
    }
  }
}

}  // namespace

LayoutFragmentChild::LayoutFragmentChild() = default;
LayoutFragmentChild::LayoutFragmentChild(const LayoutFragmentChild&) = default;
LayoutFragmentChild::LayoutFragmentChild(LayoutFragmentChild&&) = default;
LayoutFragmentChild& LayoutFragmentChild::operator=(
    const LayoutFragmentChild&) = default;
LayoutFragmentChild& LayoutFragmentChild::operator=(LayoutFragmentChild&&) =
    default;
LayoutFragmentChild::~LayoutFragmentChild() = default;

LayoutBoxFragment::LayoutBoxFragment() = default;
LayoutBoxFragment::LayoutBoxFragment(const LayoutBoxFragment&) = default;
LayoutBoxFragment::LayoutBoxFragment(LayoutBoxFragment&&) = default;
LayoutBoxFragment& LayoutBoxFragment::operator=(const LayoutBoxFragment&) =
    default;
LayoutBoxFragment& LayoutBoxFragment::operator=(LayoutBoxFragment&&) = default;
LayoutBoxFragment::~LayoutBoxFragment() = default;

LayoutBoxFragments::LayoutBoxFragments() = default;
LayoutBoxFragments::LayoutBoxFragments(const LayoutBoxFragments&) = default;
LayoutBoxFragments::LayoutBoxFragments(LayoutBoxFragments&&) = default;
LayoutBoxFragments& LayoutBoxFragments::operator=(const LayoutBoxFragments&) =
    default;
LayoutBoxFragments& LayoutBoxFragments::operator=(LayoutBoxFragments&&) =
    default;
LayoutBoxFragments::~LayoutBoxFragments() = default;

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
  hasher.Integer(node.custom_properties.size());
  for (const LayoutCheckpointStyleValue& value : node.custom_properties) {
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
  const LayoutBoxFragments& fragments = node.box_fragments;
  hasher.Boolean(fragments.present);
  hasher.Number(fragments.effective_zoom);
  hasher.Integer(fragments.fragments.size());
  for (const LayoutBoxFragment& fragment : fragments.fragments) {
    HashBoxFragment(hasher, fragment);
  }
  hasher.Boolean(fragments.natural_size_present);
  hasher.Number(fragments.natural_width);
  hasher.Number(fragments.natural_height);
  hasher.Boolean(fragments.natural_has_width);
  hasher.Boolean(fragments.natural_has_height);
  hasher.Number(fragments.natural_aspect_ratio_width);
  hasher.Number(fragments.natural_aspect_ratio_height);
  hasher.Boolean(changed.geometry_present);
  hasher.Integer(changed.transform_node_id);
  hasher.Boolean(changed.client_rect_empty);
  hasher.Boolean(changed.local_rect_mapped);
  hasher.Number(changed.local_x);
  hasher.Number(changed.local_y);
  hasher.Number(changed.local_width);
  hasher.Number(changed.local_height);
  hasher.Integer(changed.local_quad_rects.size());
  for (const LayoutLocalRect& rect : changed.local_quad_rects) {
    hasher.Number(rect.x);
    hasher.Number(rect.y);
    hasher.Number(rect.width);
    hasher.Number(rect.height);
  }
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

LayoutChangeFilter::LayoutChangeFilter() = default;
LayoutChangeFilter::~LayoutChangeFilter() = default;

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

void LayoutChangeFilter::ReduceToStyleChanges(LayoutChangedNode& changed) {
  LayoutCheckpointNode& node = changed.node;
  changed.computed_style_complete = true;
  changed.removed_custom_properties.clear();
  if (!node.computed_style_present) {
    styles_.erase(node.node_id);
    return;
  }
  StyleHashes current;
  current.values.reserve(node.computed_style.size());
  for (const LayoutCheckpointStyleValue& value : node.computed_style) {
    current.values.push_back(HashStyleValue(value));
  }
  for (const LayoutCheckpointStyleValue& value : node.custom_properties) {
    current.custom_properties[value.property_name] = HashStyleValue(value);
  }
  auto [found, inserted] = styles_.try_emplace(node.node_id);
  StyleHashes& last = found->second;
  if (inserted || last.values.size() != current.values.size()) {
    last = std::move(current);
    return;
  }
  changed.computed_style_complete = false;
  std::vector<LayoutCheckpointStyleValue> values;
  for (size_t index = 0; index < node.computed_style.size(); ++index) {
    if (current.values[index] != last.values[index]) {
      values.push_back(std::move(node.computed_style[index]));
    }
  }
  node.computed_style = std::move(values);
  std::vector<LayoutCheckpointStyleValue> custom_properties;
  for (LayoutCheckpointStyleValue& value : node.custom_properties) {
    auto kept = last.custom_properties.find(value.property_name);
    if (kept == last.custom_properties.end() ||
        kept->second != current.custom_properties[value.property_name]) {
      custom_properties.push_back(std::move(value));
    }
  }
  node.custom_properties = std::move(custom_properties);
  for (const auto& [name, hash] : last.custom_properties) {
    if (!current.custom_properties.contains(name)) {
      changed.removed_custom_properties.push_back(name);
    }
  }
  std::sort(changed.removed_custom_properties.begin(),
            changed.removed_custom_properties.end());
  last = std::move(current);
}

void LayoutChangeFilter::ForgetStyles() {
  styles_.clear();
}

}  // namespace a11y_recorder
