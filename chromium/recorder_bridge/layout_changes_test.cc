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
  Expect(differs([](auto& n) { n.transform_node_id = 4; }), "transform node");
  Expect(differs([](auto& n) { n.local_y = 17; }), "rectangle");
  Expect(differs([](auto& n) { n.client_rect_scale = 2; }), "scale");
  Expect(differs([](auto& n) { n.client_rect_empty = true; }), "empty");
  Expect(differs([](auto& n) { n.local_rect_mapped = false; }), "mapped");
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

}  // namespace

int main() {
  TestEveryStatedFieldChangesTheNodeHash();
  TestTheReasonsDoNotChangeTheNodeHash();
  TestEveryTransformFieldChangesItsHash();
  TestTheFilterPassesOnlyNewRecords();
  if (failures == 0) {
    std::printf("layout change tests passed\n");
  }
  return failures == 0 ? 0 : 1;
}
