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
  {
    // Every field of the box fragments, including a nested fragment.
    a11y_recorder::LayoutChangedNode boxed = SampleNode();
    boxed.node.box_fragments.present = true;
    a11y_recorder::LayoutBoxFragment fragment;
    fragment.width = 6400;
    fragment.height = 1280;
    a11y_recorder::LayoutFragmentChild child;
    child.kind = "box";
    child.node_id = 9;
    child.fragment_index = 0;
    a11y_recorder::LayoutFragmentChild anonymous;
    anonymous.kind = "anonymous";
    anonymous.fragment.emplace_back();
    anonymous.fragment.back().width = 64;
    fragment.children = {child, anonymous};
    boxed.node.box_fragments.fragments.push_back(fragment);
    const uint64_t boxed_hash = a11y_recorder::HashLayoutChangedNode(boxed);
    Expect(boxed_hash != a11y_recorder::HashLayoutChangedNode(SampleNode()),
           "box fragments present");
    auto box_differs = [&boxed, boxed_hash](auto change) {
      a11y_recorder::LayoutChangedNode node = boxed;
      change(node.node.box_fragments);
      return a11y_recorder::HashLayoutChangedNode(node) != boxed_hash;
    };
    Expect(box_differs([](auto& f) { f.effective_zoom = 2; }), "zoom");
    Expect(box_differs([](auto& f) { f.fragments[0].width = 6464; }),
           "fragment width");
    Expect(box_differs([](auto& f) { f.fragments[0].break_token_present = true; }),
           "break token");
    Expect(box_differs([](auto& f) { f.fragments[0].sequence_number = 1; }),
           "sequence number");
    Expect(box_differs([](auto& f) {
             f.fragments[0].scrollable_overflow_present = true;
           }),
           "scrollable overflow");
    Expect(box_differs([](auto& f) { f.fragments[0].children[0].y = 64; }),
           "child offset");
    Expect(box_differs([](auto& f) { f.fragments[0].children[0].node_id = 10; }),
           "child node");
    Expect(box_differs([](auto& f) {
             f.fragments[0].children[0].fragment_index = 1;
           }),
           "child fragment index");
    Expect(box_differs([](auto& f) {
             f.fragments[0].children[1].fragment[0].width = 128;
           }),
           "nested fragment");
    Expect(box_differs([](auto& f) { f.fragments.push_back({}); }),
           "fragment count");
    Expect(box_differs([](auto& f) { f.natural_size_present = true; }),
           "natural size");
    Expect(box_differs([](auto& f) { f.natural_aspect_ratio_height = 3; }),
           "natural aspect ratio");
  }
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

a11y_recorder::LayoutChangedNode TextNodeBlock(const std::string& text) {
  a11y_recorder::LayoutChangedNode block = SampleNode();
  block.node.box_fragments.present = true;
  block.node.box_fragments.text_present = true;
  block.node.box_fragments.text_content = text;
  return block;
}

void TestItemsTextAndGlyphsChangeTheNodeHash() {
  a11y_recorder::LayoutChangedNode block = TextNodeBlock("Hello");
  a11y_recorder::LayoutBoxFragment fragment;
  fragment.items_present = true;
  a11y_recorder::LayoutFragmentItem line;
  line.type = "line";
  line.descendants_count = 2;
  a11y_recorder::LayoutFragmentItem text;
  text.type = "text";
  text.text = true;
  text.range_present = true;
  text.end = 5;
  a11y_recorder::LayoutGlyphRun run;
  run.family = "Arial";
  run.glyphs.push_back({43, 0, 0, 0, 0});
  text.glyph_runs.push_back(run);
  fragment.items = {line, text};
  block.node.box_fragments.fragments.push_back(fragment);
  const uint64_t base = a11y_recorder::HashLayoutChangedNode(block);
  auto differs = [&block, base](auto change) {
    a11y_recorder::LayoutChangedNode node = block;
    change(node.node.box_fragments);
    return a11y_recorder::HashLayoutChangedNode(node) != base;
  };
  Expect(differs([](auto& f) { f.text_content = "Hellp"; }), "text content");
  Expect(differs([](auto& f) { f.first_line_text_present = true; }),
         "first line text");
  Expect(differs([](auto& f) { f.fragments[0].items[1].end = 4; }),
         "item range");
  Expect(differs([](auto& f) { f.fragments[0].items[0].descendants_count = 1; }),
         "item descendants");
  Expect(differs([](auto& f) { f.fragments[0].items[1].rtl = true; }),
         "item direction");
  Expect(differs([](auto& f) {
           f.fragments[0].items[1].glyph_runs[0].family = "Verdana";
         }),
         "run font");
  Expect(differs([](auto& f) {
           f.fragments[0].items[1].glyph_runs[0].font_file_digest = "ab";
         }),
         "run font file");
  Expect(differs([](auto& f) {
           f.fragments[0].items[1].glyph_runs[0].font_file_index = 1;
         }),
         "run font file index");
  Expect(differs([](auto& f) {
           f.fragments[0].items[1].glyph_runs[0].font_variations.push_back(
               {0x77676874, 700.0f});
         }),
         "run font variation");
  Expect(differs([](auto& f) {
           f.fragments[0].items[1].glyph_runs[0].glyphs[0].total_advance =
               0.25f;
         }),
         "glyphs");
  Expect(differs([](auto& f) { f.fragments[0].items.pop_back(); }),
         "item count");
  Expect(differs([](auto& f) { f.fragments[0].text_content = "Anonymous"; }),
         "a held fragment's text");
}

void TestGlyphsArePackedLittleEndian() {
  const std::string glyphs = a11y_recorder::PackGlyphs(
      {{0x1234, 0x00070001, 1.0f, -0.5f, 2.0f}});
  Expect(glyphs.size() == a11y_recorder::kPackedGlyphBytes, "glyph size");
  const unsigned char expected[] = {
      0x34, 0x12,              // glyph
      0x01, 0x00, 0x07, 0x00,  // character index
      0x00, 0x00, 0x80, 0x3f,  // 1.0f
      0x00, 0x00, 0x00, 0xbf,  // -0.5f
      0x00, 0x00, 0x00, 0x40,  // 2.0f
  };
  bool same = glyphs.size() == sizeof(expected);
  for (size_t index = 0; same && index < sizeof(expected); ++index) {
    same = static_cast<unsigned char>(glyphs[index]) == expected[index];
  }
  Expect(same, "glyph bytes");
}

void TestTextIsLeftOutWhenItEqualsTheLastRecord() {
  a11y_recorder::LayoutChangeFilter filter;
  a11y_recorder::LayoutChangedNode first = TextNodeBlock("Hello");
  filter.ReduceToTextChanges(first);
  Expect(!first.node.box_fragments.text_unchanged &&
             first.node.box_fragments.text_content == "Hello",
         "a first record holds its text");
  a11y_recorder::LayoutChangedNode same = TextNodeBlock("Hello");
  filter.ReduceToTextChanges(same);
  Expect(same.node.box_fragments.text_unchanged &&
             same.node.box_fragments.text_content.empty(),
         "an equal text is left out");
  a11y_recorder::LayoutChangedNode other = TextNodeBlock("Help");
  filter.ReduceToTextChanges(other);
  Expect(!other.node.box_fragments.text_unchanged &&
             other.node.box_fragments.text_content == "Help",
         "a changed text is recorded");
  a11y_recorder::LayoutChangedNode first_line = TextNodeBlock("Help");
  first_line.node.box_fragments.first_line_text_present = true;
  first_line.node.box_fragments.first_line_text = "HELP";
  filter.ReduceToTextChanges(first_line);
  Expect(!first_line.node.box_fragments.text_unchanged,
         "a new first-line text is recorded");
  a11y_recorder::LayoutChangedNode none = SampleNode();
  filter.ReduceToTextChanges(none);
  a11y_recorder::LayoutChangedNode again = TextNodeBlock("Help");
  filter.ReduceToTextChanges(again);
  Expect(!again.node.box_fragments.text_unchanged,
         "a record without text forgets the hash");
  filter.ForgetStyles();
  a11y_recorder::LayoutChangedNode after_loss = TextNodeBlock("Help");
  filter.ReduceToTextChanges(after_loss);
  Expect(!after_loss.node.box_fragments.text_unchanged,
         "text is recorded whole after a loss");
}

int main() {
  TestItemsTextAndGlyphsChangeTheNodeHash();
  TestGlyphsArePackedLittleEndian();
  TestTextIsLeftOutWhenItEqualsTheLastRecord();
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
