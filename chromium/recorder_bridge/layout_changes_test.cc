// Checks the layout change hashes and filter without a Chromium build. The
// test suite in chromium/test_integrate.py compiles and runs this file when a
// C++ compiler is available, since they depend on the standard library alone.

#include "chromium/recorder_bridge/layout_changes.h"

#include <cstdio>

namespace {

int failures = 0;

void Expect(bool condition, const char* description) {
  if (!condition) {
    std::fprintf(stderr, "FAILED: %s\n", description);
    ++failures;
  }
}

a11y_recorder::LayoutChangedNode SampleNode() {
  a11y_recorder::LayoutChangedNode changed;
  changed.node.node_id = 7;
  changed.node.node_type = 1;
  changed.node.node_name = "DIV";
  changed.node.layout_object_present = true;
  changed.node.computed_style_present = true;
  changed.node.computed_style.push_back({"color", true, "rgb(0, 0, 0)"});
  changed.node.computed_style.push_back({"display", true, "block"});
  changed.geometry_present = true;
  changed.transform_node_id = 3;
  changed.local_rect_mapped = true;
  changed.local_x = 8;
  changed.local_y = 16;
  changed.local_width = 100;
  changed.local_height = 20;
  return changed;
}

void TestEveryStatedFieldChangesTheNodeHash() {
  const uint64_t base = a11y_recorder::HashLayoutChangedNode(SampleNode());
  auto differs = [base](auto change) {
    a11y_recorder::LayoutChangedNode node = SampleNode();
    change(node);
    return a11y_recorder::HashLayoutChangedNode(node) != base;
  };
  Expect(differs([](auto& n) { n.node.node_name = "SPAN"; }), "name");
  Expect(differs([](auto& n) { n.node.display_locked = true; }), "lock");
  Expect(differs([](auto& n) { n.node.computed_style[0].value = "red"; }),
         "style value");
  Expect(differs([](auto& n) { n.node.computed_style[1].value_present = false; }),
         "style presence");
  Expect(differs([](auto& n) { n.node.computed_style.pop_back(); }),
         "style count");
  Expect(differs([](auto& n) {
           n.node.custom_properties.push_back({"--gap", true, "4px"});
         }),
         "custom property");
  Expect(differs([](auto& n) { n.transform_node_id = 4; }), "transform node");
  Expect(differs([](auto& n) { n.local_y = 17; }), "rectangle");
  Expect(differs([](auto& n) { n.client_rect_scale = 2; }), "scale");
  Expect(differs([](auto& n) { n.client_rect_empty = true; }), "empty");
  Expect(differs([](auto& n) { n.local_rect_mapped = false; }), "mapped");
  Expect(differs([](auto& n) {
           n.local_quad_rects = {{0, 0, 10, 5}, {0, 5, 6, 5}};
         }),
         "quad rectangles");
  {
    a11y_recorder::LayoutChangedNode one = SampleNode();
    one.local_quad_rects = {{0, 0, 10, 5}, {0, 5, 6, 5}};
    a11y_recorder::LayoutChangedNode other = one;
    other.local_quad_rects[1].width = 7;
    Expect(a11y_recorder::HashLayoutChangedNode(one) !=
               a11y_recorder::HashLayoutChangedNode(other),
           "quad rectangle width");
  }
  Expect(differs([](auto& n) { n.node.shadow_root_mode = "open"; }), "shadow");
  Expect(differs([](auto& n) { n.node.generated_text = "x"; }), "generated");
  // The field boundaries are part of the hash.
  Expect(differs([](auto& n) {
           n.node.computed_style[0].property_name = "colo";
           n.node.computed_style[0].value = "rrgb(0, 0, 0)";
         }),
         "boundaries");
}

void TestTheReasonsDoNotChangeTheNodeHash() {
  a11y_recorder::LayoutChangedNode node = SampleNode();
  const uint64_t base = a11y_recorder::HashLayoutChangedNode(node);
  node.reasons = a11y_recorder::kLayoutChangeStyle |
                 a11y_recorder::kLayoutChangePaintProperties;
  Expect(a11y_recorder::HashLayoutChangedNode(node) == base, "reasons");
}

void TestEveryTransformFieldChangesItsHash() {
  a11y_recorder::LayoutTransformNode node;
  node.id = 2;
  node.parent_id = 1;
  node.matrix = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -120, 0, 1};
  const uint64_t base = a11y_recorder::HashLayoutTransformNode(node);
  a11y_recorder::LayoutTransformNode scrolled = node;
  scrolled.matrix[13] = -121;
  Expect(a11y_recorder::HashLayoutTransformNode(scrolled) != base, "matrix");
  a11y_recorder::LayoutTransformNode reparented = node;
  reparented.parent_id = 5;
  Expect(a11y_recorder::HashLayoutTransformNode(reparented) != base, "parent");
  a11y_recorder::LayoutTransformNode sticky = node;
  sticky.sticky = true;
  Expect(a11y_recorder::HashLayoutTransformNode(sticky) != base, "sticky");
}

void TestTheFilterPassesOnlyNewRecords() {
  a11y_recorder::LayoutChangeFilter filter;
  Expect(filter.NodeChanged(7, 100), "first record");
  Expect(!filter.NodeChanged(7, 100), "same record");
  Expect(filter.NodeChanged(7, 101), "changed record");
  Expect(!filter.NodeChanged(7, 101), "changed record kept");
  Expect(filter.NodeChanged(7, 100), "earlier record again");
  Expect(filter.NodeChanged(8, 100), "another node");
  Expect(filter.TransformNodeChanged(7, 100), "transform apart from nodes");
  Expect(!filter.TransformNodeChanged(7, 100), "same transform");
  Expect(filter.TransformNodeChanged(7, 5), "changed transform");
}

a11y_recorder::LayoutChangedNode StyledNode(const char* color,
                                            const char* gap,
                                            const char* accent) {
  a11y_recorder::LayoutChangedNode changed = SampleNode();
  changed.node.computed_style[0].value = color;
  if (gap) {
    changed.node.custom_properties.push_back({"--gap", true, gap});
  }
  if (accent) {
    changed.node.custom_properties.push_back({"--accent", true, accent});
  }
  return changed;
}

void TestOnlyChangedStyleValuesFollowAFirstRecord() {
  a11y_recorder::LayoutChangeFilter filter;
  a11y_recorder::LayoutChangedNode first = StyledNode("red", "4px", nullptr);
  filter.ReduceToStyleChanges(first);
  Expect(first.computed_style_complete, "first record complete");
  Expect(first.node.computed_style.size() == 2, "first record every value");
  Expect(first.node.custom_properties.size() == 1, "first record custom");

  a11y_recorder::LayoutChangedNode same = StyledNode("red", "4px", nullptr);
  filter.ReduceToStyleChanges(same);
  Expect(!same.computed_style_complete, "same style partial");
  Expect(same.node.computed_style.empty(), "same style no values");
  Expect(same.node.custom_properties.empty(), "same style no custom");
  Expect(same.removed_custom_properties.empty(), "same style no removal");

  a11y_recorder::LayoutChangedNode changed =
      StyledNode("blue", nullptr, "green");
  filter.ReduceToStyleChanges(changed);
  Expect(!changed.computed_style_complete, "changed style partial");
  Expect(changed.node.computed_style.size() == 1 &&
             changed.node.computed_style[0].property_name == "color" &&
             changed.node.computed_style[0].value == "blue",
         "changed value only");
  Expect(changed.node.custom_properties.size() == 1 &&
             changed.node.custom_properties[0].property_name == "--accent",
         "added custom property");
  Expect(changed.removed_custom_properties.size() == 1 &&
             changed.removed_custom_properties[0] == "--gap",
         "removed custom property");

  a11y_recorder::LayoutChangedNode unstyled = SampleNode();
  unstyled.node.computed_style_present = false;
  unstyled.node.computed_style.clear();
  filter.ReduceToStyleChanges(unstyled);
  Expect(unstyled.computed_style_complete, "no style complete");
  a11y_recorder::LayoutChangedNode restyled = StyledNode("blue", nullptr, "green");
  filter.ReduceToStyleChanges(restyled);
  Expect(restyled.computed_style_complete, "style after none complete");
  Expect(restyled.node.computed_style.size() == 2, "style after none whole");

  a11y_recorder::LayoutChangedNode shorter = StyledNode("blue", nullptr, "green");
  shorter.node.computed_style.pop_back();
  filter.ReduceToStyleChanges(shorter);
  Expect(shorter.computed_style_complete, "other property count complete");

  filter.ForgetStyles();
  Expect(filter.StyleNodeCount() == 0, "styles forgotten");
  a11y_recorder::LayoutChangedNode forgotten =
      StyledNode("blue", nullptr, "green");
  filter.ReduceToStyleChanges(forgotten);
  Expect(forgotten.computed_style_complete, "record after forgetting complete");
}

}  // namespace

int main() {
  TestEveryStatedFieldChangesTheNodeHash();
  TestTheReasonsDoNotChangeTheNodeHash();
  TestEveryTransformFieldChangesItsHash();
  TestTheFilterPassesOnlyNewRecords();
  TestOnlyChangedStyleValuesFollowAFirstRecord();
  if (failures == 0) {
    std::printf("layout change tests passed\n");
  }
  return failures == 0 ? 0 : 1;
}
