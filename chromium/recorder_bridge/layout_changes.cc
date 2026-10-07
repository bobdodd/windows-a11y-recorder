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

void HashFragmentItem(Hasher& hasher, const LayoutFragmentItem& item) {
  hasher.Text(item.type);
  hasher.Number(item.x);
  hasher.Number(item.y);
  hasher.Number(item.width);
  hasher.Number(item.height);
  hasher.Integer(static_cast<uint64_t>(item.descendants_count));
  hasher.Integer(static_cast<uint64_t>(item.node_id));
  hasher.Boolean(item.text);
  hasher.Boolean(item.range_present);
  hasher.Integer(item.start);
  hasher.Integer(item.end);
  hasher.Boolean(item.first_line_style);
  hasher.Boolean(item.rtl);
  hasher.Boolean(item.hidden_for_paint);
  hasher.Boolean(item.generated_text_present);
  hasher.Text(item.generated_text);
  hasher.Integer(item.glyph_runs.size());
  for (const LayoutGlyphRun& run : item.glyph_runs) {
    hasher.Text(run.family);
    hasher.Text(run.post_script_name);
    hasher.Number(run.size);
    hasher.Boolean(run.synthetic_bold);
    hasher.Boolean(run.synthetic_italic);
    hasher.Boolean(run.font_file_present);
    hasher.Text(run.font_file_digest);
    hasher.Integer(static_cast<uint64_t>(run.font_file_index));
    hasher.Integer(run.font_variations.size());
    for (const LayoutFontVariation& variation : run.font_variations) {
      hasher.Integer(variation.axis);
      hasher.Integer(std::bit_cast<uint32_t>(variation.value));
    }
    hasher.Boolean(run.horizontal);
    hasher.Integer(static_cast<uint64_t>(run.rotation));
    hasher.Integer(run.glyphs.size());
    for (const LayoutGlyph& glyph : run.glyphs) {
      hasher.Integer(glyph.glyph);
      hasher.Integer(glyph.character_index);
      hasher.Integer(std::bit_cast<uint32_t>(glyph.total_advance));
      hasher.Integer(std::bit_cast<uint32_t>(glyph.offset_x));
      hasher.Integer(std::bit_cast<uint32_t>(glyph.offset_y));
    }
  }
}

void AppendLittleEndian(std::string& bytes, uint32_t value, int count) {
  for (int index = 0; index < count; ++index) {
    bytes.push_back(static_cast<char>((value >> (8 * index)) & 0xff));
  }
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
  hasher.Boolean(fragment.text_present);
  hasher.Text(fragment.text_content);
  hasher.Boolean(fragment.first_line_text_present);
  hasher.Text(fragment.first_line_text);
  hasher.Boolean(fragment.items_present);
  hasher.Integer(fragment.items.size());
  for (const LayoutFragmentItem& item : fragment.items) {
    HashFragmentItem(hasher, item);
  }
}

uint64_t HashText(const LayoutBoxFragments& fragments) {
  Hasher hasher;
  hasher.Text(fragments.text_content);
  hasher.Boolean(fragments.first_line_text_present);
  hasher.Text(fragments.first_line_text);
  return hasher.Value();
}

}  // namespace

std::string PackGlyphs(const std::vector<LayoutGlyph>& glyphs) {
  std::string bytes;
  bytes.reserve(glyphs.size() * kPackedGlyphBytes);
  for (const LayoutGlyph& glyph : glyphs) {
    AppendLittleEndian(bytes, glyph.glyph, 2);
    AppendLittleEndian(bytes, glyph.character_index, 4);
    AppendLittleEndian(bytes, std::bit_cast<uint32_t>(glyph.total_advance), 4);
    AppendLittleEndian(bytes, std::bit_cast<uint32_t>(glyph.offset_x), 4);
    AppendLittleEndian(bytes, std::bit_cast<uint32_t>(glyph.offset_y), 4);
  }
  return bytes;
}

LayoutGlyphRun::LayoutGlyphRun() = default;
LayoutGlyphRun::LayoutGlyphRun(const LayoutGlyphRun&) = default;
LayoutGlyphRun::LayoutGlyphRun(LayoutGlyphRun&&) = default;
LayoutGlyphRun& LayoutGlyphRun::operator=(const LayoutGlyphRun&) = default;
LayoutGlyphRun& LayoutGlyphRun::operator=(LayoutGlyphRun&&) = default;
LayoutGlyphRun::~LayoutGlyphRun() = default;

LayoutFragmentItem::LayoutFragmentItem() = default;
LayoutFragmentItem::LayoutFragmentItem(const LayoutFragmentItem&) = default;
LayoutFragmentItem::LayoutFragmentItem(LayoutFragmentItem&&) = default;
LayoutFragmentItem& LayoutFragmentItem::operator=(const LayoutFragmentItem&) =
    default;
LayoutFragmentItem& LayoutFragmentItem::operator=(LayoutFragmentItem&&) =
    default;
LayoutFragmentItem::~LayoutFragmentItem() = default;

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
  hasher.Boolean(fragments.text_present);
  hasher.Text(fragments.text_content);
  hasher.Boolean(fragments.first_line_text_present);
  hasher.Text(fragments.first_line_text);
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

void LayoutChangeFilter::ReduceToTextChanges(LayoutChangedNode& changed) {
  LayoutBoxFragments& fragments = changed.node.box_fragments;
  fragments.text_unchanged = false;
  if (!fragments.present || !fragments.text_present) {
    texts_.erase(changed.node.node_id);
    return;
  }
  const uint64_t hash = HashText(fragments);
  auto [found, inserted] = texts_.try_emplace(changed.node.node_id, hash);
  if (inserted) {
    return;
  }
  if (found->second != hash) {
    found->second = hash;
    return;
  }
  fragments.text_unchanged = true;
  fragments.text_content.clear();
  fragments.first_line_text.clear();
}

void LayoutChangeFilter::ForgetStyles() {
  styles_.clear();
  texts_.clear();
}

}  // namespace a11y_recorder
