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
};

// The reasons a node was noted, as bits.
enum LayoutChangeReason : unsigned {
  kLayoutChangeStyle = 1u << 0,
  kLayoutChangeLayout = 1u << 1,
  kLayoutChangePaintProperties = 1u << 2,
};

// A rectangle in a transform node's space.
struct LayoutLocalRect {
  double x = 0;
  double y = 0;
  double width = 0;
  double height = 0;
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
// layout zoom factor.
struct LayoutChangesFrame {
  uint64_t view_transform_node_id = 0;
  double view_paint_offset_x = 0;
  double view_paint_offset_y = 0;
  double layout_zoom_factor = 0;
};

// One scroller whose scroll offset Blink stored during a rendering update,
// read at the end of the update. The node is the scroller's element, or the
// document for the frame's own scroller. The scroll offset is the one
// PaintLayerScrollableArea holds, the web-exposed offset is the one
// scrollLeft and scrollTop divide by the effective zoom, and the scroll
// origin is the position of offset zero. The scroll translation is the
// transform node the offset moves, or zero when the scroller has none.
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

  // Forgets every kept style value, so the next record of each node holds
  // its whole computed style. Used after a record may have been lost.
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
  std::unordered_map<uint64_t, uint64_t> transform_nodes_;
};

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_LAYOUT_CHANGES_H_
