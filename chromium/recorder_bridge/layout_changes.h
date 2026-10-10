#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_LAYOUT_CHANGES_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_LAYOUT_CHANGES_H_

#include <array>
#include <cstdint>
#include <string>
#include <unordered_map>
#include <vector>

// The records of layout checkpoints and of layout change sets, and the filter
// that finds a change record equal to the last record of its node. They use
// the C++ standard library alone, so the filter can be exercised outside a
// Chromium build, as the evidence queue is.
namespace a11y_recorder {

// One computed-style value of an element. An absent value means Blink
// produced no serialization for the property from the element's style.
struct LayoutCheckpointStyleValue {
  std::string property_name;
  bool value_present = false;
  std::string value;
};

// A rectangle in a transform node's space, or in a fragment's own space.
struct LayoutLocalRect {
  double x = 0;
  double y = 0;
  double width = 0;
  double height = 0;
};

struct LayoutBoxFragment;

// One glyph as ShapeResultView::ForEachGlyph reports it: its identifier, the
// character index, an index into the block's text content, the total advance
// before it, and its offset.
struct LayoutGlyph {
  uint16_t glyph = 0;
  uint32_t character_index = 0;
  float total_advance = 0;
  float offset_x = 0;
  float offset_y = 0;
};

// One run of glyphs of a text item with the same font, orientation, and
// rotation (protocol 0.39). The bridge records the glyphs packed
// little-endian, 18 bytes each, by PackGlyphs.
// One axis of a typeface's variation position (protocol 0.40): the axis tag,
// four characters packed big-endian as Skia's SkFourByteTag, and its value.
struct LayoutFontVariation {
  uint32_t axis = 0;
  float value = 0;
};

struct LayoutGlyphRun {
  LayoutGlyphRun();
  LayoutGlyphRun(const LayoutGlyphRun&);
  LayoutGlyphRun(LayoutGlyphRun&&);
  LayoutGlyphRun& operator=(const LayoutGlyphRun&);
  LayoutGlyphRun& operator=(LayoutGlyphRun&&);
  ~LayoutGlyphRun();

  std::string family;
  std::string post_script_name;
  double size = 0;
  bool synthetic_bold = false;
  bool synthetic_italic = false;
  // The run's font file (protocol 0.40): the digest of the file's bytes, its
  // index in a font collection, and the typeface's variation position. The
  // file is absent when Skia gives no stream for the typeface.
  bool font_file_present = false;
  std::string font_file_digest;
  int font_file_index = 0;
  std::vector<LayoutFontVariation> font_variations;
  bool horizontal = true;
  int rotation = 0;
  std::vector<LayoutGlyph> glyphs;
};

// A run's glyphs packed little-endian, 18 bytes each: the identifier (2
// bytes), the character index (4 bytes), the total advance before the glyph,
// and the offset, x and y (4-byte floats each).
std::string PackGlyphs(const std::vector<LayoutGlyph>& glyphs);
inline constexpr size_t kPackedGlyphBytes = 18;

// One fragment item of a block fragment (protocol 0.39): its type, "line",
// "text", "generated-text", or "box"; its rectangle in the fragment, in
// layout units; for a line or box item, the number of items it spans, itself
// included, or -1 otherwise; and the node of its layout object, or 0. A text
// or generated-text item has its direction, style variant, paint flag, and
// glyph runs; a text item its range of the block's text content; and a
// generated-text item its own text.
struct LayoutFragmentItem {
  LayoutFragmentItem();
  LayoutFragmentItem(const LayoutFragmentItem&);
  LayoutFragmentItem(LayoutFragmentItem&&);
  LayoutFragmentItem& operator=(const LayoutFragmentItem&);
  LayoutFragmentItem& operator=(LayoutFragmentItem&&);
  ~LayoutFragmentItem();

  std::string type;
  double x = 0;
  double y = 0;
  double width = 0;
  double height = 0;
  int descendants_count = -1;
  int node_id = 0;
  bool text = false;
  bool range_present = false;
  uint32_t start = 0;
  uint32_t end = 0;
  bool first_line_style = false;
  bool rtl = false;
  bool hidden_for_paint = false;
  bool generated_text_present = false;
  std::string generated_text;
  std::vector<LayoutGlyphRun> glyph_runs;
};

// One child link of a box fragment (protocol 0.38): its kind, "box" for a box
// with a DOM node, "anonymous" for a box with none, "column" or "page" for a
// fragmentainer, or "line" for a line box; its offset in the parent fragment;
// for a box with a node, the node and which of its fragments this is, or -1
// when it was not found among them; and for an anonymous box, column, or
// page, its own fragment, as the one element of the fragment list.
struct LayoutFragmentChild {
  LayoutFragmentChild();
  LayoutFragmentChild(const LayoutFragmentChild&);
  LayoutFragmentChild(LayoutFragmentChild&&);
  LayoutFragmentChild& operator=(const LayoutFragmentChild&);
  LayoutFragmentChild& operator=(LayoutFragmentChild&&);
  ~LayoutFragmentChild();

  std::string kind;
  int node_id = 0;
  int fragment_index = -1;
  double x = 0;
  double y = 0;
  std::vector<LayoutBoxFragment> fragment;
};

// One physical fragment of a layout box (protocol 0.38): its border-box size;
// the position its next fragment continues from, when it has a break token;
// its scrollable overflow, when it has one; and its child links in order.
// Lengths are Blink's layout units, physical and zoomed. The sequence number
// is only meaningful for a break token that is not a break before.
struct LayoutBoxFragment {
  LayoutBoxFragment();
  LayoutBoxFragment(const LayoutBoxFragment&);
  LayoutBoxFragment(LayoutBoxFragment&&);
  LayoutBoxFragment& operator=(const LayoutBoxFragment&);
  LayoutBoxFragment& operator=(LayoutBoxFragment&&);
  ~LayoutBoxFragment();

  double width = 0;
  double height = 0;
  bool break_token_present = false;
  double consumed_block_size = 0;
  bool break_before = false;
  unsigned sequence_number = 0;
  bool at_block_end = false;
  bool scrollable_overflow_present = false;
  LayoutLocalRect scrollable_overflow;
  std::vector<LayoutFragmentChild> children;
  // The fragment's items, when it holds lines (protocol 0.39).
  bool items_present = false;
  std::vector<LayoutFragmentItem> items;
  // The text content of a fragment that holds lines and is held by a child
  // link, such as an anonymous block, whose items index it, in UTF-8. A
  // node's own fragments leave it to LayoutBoxFragments.
  bool text_present = false;
  std::string text_content;
  bool first_line_text_present = false;
  std::string first_line_text;
};

// The fragments of a node whose layout object is a layout box (protocol
// 0.38), with the box's effective zoom, and a replaced element's natural
// dimensions. Not present for any other node.
struct LayoutBoxFragments {
  LayoutBoxFragments();
  LayoutBoxFragments(const LayoutBoxFragments&);
  LayoutBoxFragments(LayoutBoxFragments&&);
  LayoutBoxFragments& operator=(const LayoutBoxFragments&);
  LayoutBoxFragments& operator=(LayoutBoxFragments&&);
  ~LayoutBoxFragments();

  bool present = false;
  double effective_zoom = 1;
  std::vector<LayoutBoxFragment> fragments;
  bool natural_size_present = false;
  double natural_width = 0;
  double natural_height = 0;
  bool natural_has_width = false;
  bool natural_has_height = false;
  double natural_aspect_ratio_width = 0;
  double natural_aspect_ratio_height = 0;
  // The block's text content as laid out, and its ::first-line text when
  // Blink holds one (protocol 0.39), in UTF-8. Set by
  // LayoutChangeFilter::ReduceToTextChanges, text_unchanged means the text
  // equals the node's last record and is left out.
  bool text_present = false;
  std::string text_content;
  bool first_line_text_present = false;
  std::string first_line_text;
  bool text_unchanged = false;
};

// One element or text node at a layout checkpoint. The rectangle is the value
// getBoundingClientRect would return at the checkpoint, in CSS pixels relative
// to the frame's viewport, and is only meaningful when a layout object exists.
// A display-locked node sits under a content-visibility ancestor that skipped
// its layout, so its rectangle may be stale. The computed style is the style
// Blink already held for the element; the checkpoint never computes one.
struct LayoutCheckpointNode {
  int node_index = -1;
  int node_id = 0;
  int node_type = 0;
  std::string node_name;
  bool layout_object_present = false;
  bool display_locked = false;
  double x = 0;
  double y = 0;
  double width = 0;
  double height = 0;
  bool computed_style_present = false;
  std::vector<LayoutCheckpointStyleValue> computed_style;
  // The custom properties of the computed style, by name in code-unit order,
  // each with its value (protocol 0.37). Only meaningful with a computed
  // style.
  std::vector<LayoutCheckpointStyleValue> custom_properties;
  // Set for a pseudo-element. The originating node is the element or
  // pseudo-element that holds it. The generated text is the text laid out in
  // the pseudo-element's layout subtree, truncated by the caller, with its
  // full length in UTF-16 code units.
  bool pseudo_element_present = false;
  int originating_node_id = 0;
  std::string pseudo_type;
  std::string generated_text;
  int generated_text_length = 0;
  bool generated_text_truncated = false;
  // Set for a node inside a shadow tree: the host of the containing shadow
  // root and that root's mode.
  int shadow_host_node_id = 0;
  std::string shadow_root_mode;
  // The node's box fragments (protocol 0.38).
  LayoutBoxFragments box_fragments;
};

// The reasons a node was noted, as bits.
enum LayoutChangeReason : unsigned {
  kLayoutChangeStyle = 1u << 0,
  kLayoutChangeLayout = 1u << 1,
  kLayoutChangePaintProperties = 1u << 2,
};

// One node of a layout change set. The node's fields are those of a
// checkpoint record, without its index or rectangle. The geometry is the
// rectangle getBoundingClientRect is built from, before its zoom
// adjustment, in the space of the transform node its position is relative
// to. Multiplying the rectangle derived in viewport space by the client rect
// scale gives CSS pixels.
struct LayoutChangedNode {
  LayoutCheckpointNode node;
  unsigned reasons = 0;
  // Set when the node has a layout object whose transform node was found.
  bool geometry_present = false;
  uint64_t transform_node_id = 0;
  // Set when the rectangle getBoundingClientRect is built from is empty, so
  // it returns an empty rectangle at the viewport's origin.
  bool client_rect_empty = false;
  // Set when a rectangle that is not empty could be mapped into the transform
  // node's space.
  bool local_rect_mapped = false;
  double local_x = 0;
  double local_y = 0;
  double local_width = 0;
  double local_height = 0;
  // When getBoundingClientRect unites more than one quad, such as the lines
  // of a text node, the bounds of each quad in the transform node's space,
  // in Blink's order (protocol 0.36). Under a transform that rotates or
  // skews, the rectangle is the union of the bounds of each mapped quad,
  // which the local rectangle alone does not determine. Empty otherwise.
  std::vector<LayoutLocalRect> local_quad_rects;
  double client_rect_scale = 1;
  // Set by LayoutChangeFilter::ReduceToStyleChanges (protocol 0.37). When
  // false, the node's computed style and custom properties hold only the
  // values that differ from the node's last record, and the removed custom
  // properties name those its last record held and this one does not.
  bool computed_style_complete = true;
  std::vector<std::string> removed_custom_properties;
};

// One transform node of the paint property tree. The matrix is the node's
// matrix with its transform origin applied, in column-major order. A parent
// identity of zero marks the root of the tree.
struct LayoutTransformNode {
  uint64_t id = 0;
  uint64_t parent_id = 0;
  std::array<double, 16> matrix{};
  bool flattens_inherited_transform = false;
  bool scroll_translation = false;
  bool sticky = false;
};

// What a change set states about its document's view: the transform node of
// the layout view's local border box, the view's paint offset in it, and the
// layout zoom factor. From protocol 0.58, also the viewport, in CSS pixels,
// and the device pixel ratio, as a layout checkpoint reads them, so that a
// window resized after the checkpoint is recorded.
struct LayoutChangesFrame {
  uint64_t view_transform_node_id = 0;
  double view_paint_offset_x = 0;
  double view_paint_offset_y = 0;
  double layout_zoom_factor = 0;
  double viewport_width = 0;
  double viewport_height = 0;
  double device_pixel_ratio = 0;
};

// One scroller whose scroll offset Blink stored during a rendering update,
// read at the end of the update. The node is the scroller's element, or the
// document for the frame's own scroller. The scroll offset is the one
// PaintLayerScrollableArea holds, the web-exposed offset is the one
// scrollLeft and scrollTop divide by the effective zoom, and the scroll
// origin is the position of offset zero. The scroll translation is the
// transform node the offset moves, or zero when the scroller has none. From
// protocol 0.49 (slice 4b sub-step 2b-ii), the scroll element ID is the
// scroller's compositor element ID (ScrollableArea::GetScrollElementId), or
// zero when it has none.
struct LayoutScrollOffset {
  int node_id = 0;
  double scroll_offset_x = 0;
  double scroll_offset_y = 0;
  double web_exposed_scroll_offset_x = 0;
  double web_exposed_scroll_offset_y = 0;
  int scroll_origin_x = 0;
  int scroll_origin_y = 0;
  double effective_zoom = 1;
  uint64_t scroll_translation_node_id = 0;
  uint64_t scroll_element_id = 0;
};

// Hashes every field a change record states, except the reasons it was
// noted, which describe the noting and not the node.
uint64_t HashLayoutChangedNode(const LayoutChangedNode& node);
uint64_t HashLayoutTransformNode(const LayoutTransformNode& node);

// Keeps the hash of the last record of each node and each transform node.
// A record is new when its hash differs from the last one kept, or none was
// kept; the new hash is then kept.
class LayoutChangeFilter {
 public:
  LayoutChangeFilter();
  ~LayoutChangeFilter();

  bool NodeChanged(int node_id, uint64_t hash);
  bool TransformNodeChanged(uint64_t transform_node_id, uint64_t hash);

  // Reduces a changed node's computed style and custom properties to the
  // values that differ from those of the node's last record, and keeps a
  // hash of each value. The record is left complete when no values were
  // kept for the node, when the node has no computed style, whose values
  // are then forgotten, and when the number of properties differs.
  void ReduceToStyleChanges(LayoutChangedNode& changed);

  // Leaves out a changed node's text content when it equals the text of the
  // node's last record, setting text_unchanged, and keeps a hash of the text
  // otherwise (protocol 0.39). A node with no text has its hash forgotten.
  void ReduceToTextChanges(LayoutChangedNode& changed);

  // Forgets every kept style value and text hash, so the next record of each
  // node holds its whole computed style and text. Used after a record may
  // have been lost.
  void ForgetStyles();

  // The number of nodes whose style values are kept.
  size_t StyleNodeCount() const { return styles_.size(); }

 private:
  struct StyleHashes {
    std::vector<uint64_t> values;
    std::unordered_map<std::string, uint64_t> custom_properties;
  };

  std::unordered_map<int, uint64_t> nodes_;
  std::unordered_map<int, StyleHashes> styles_;
  std::unordered_map<int, uint64_t> texts_;
  std::unordered_map<uint64_t, uint64_t> transform_nodes_;
};

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_LAYOUT_CHANGES_H_
