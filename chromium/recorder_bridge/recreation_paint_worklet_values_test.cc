// Checks the reading of the paint worklet values a recreation imposes,
// without a Chromium build. The test suite in chromium/test_integrate.py
// compiles and runs this file when a C++ compiler is available.

#include "chromium/recorder_bridge/recreation_paint_worklet_values.h"

#include <cstdio>
#include <cstdlib>
#include <string>
#include <string_view>

namespace {

void Check(bool condition, const char* what) {
  if (!condition) {
    std::fprintf(stderr, "failed: %s\n", what);
    std::exit(1);
  }
}

// As base::StringToDouble: the whole text must be the number.
bool ParseNumber(std::string_view text, double* value) {
  const std::string copy(text);
  if (copy.empty() || copy.front() == ' ' || copy.back() == ' ') {
    return false;
  }
  char* end = nullptr;
  *value = std::strtod(copy.c_str(), &end);
  return end == copy.c_str() + copy.size();
}

a11y_recorder::RecreationPaintWorkletValues Parse(std::string_view text) {
  return a11y_recorder::ParseRecreationPaintWorkletValues(text, ParseNumber);
}

using a11y_recorder::RecreationPathVerb;

}  // namespace

int main() {
  Check(Parse("").empty(), "no text is no values");
  {
    const auto values = Parse(
        "background-color 0.5198276042938232 0.0 0.2801724076271057 1.0; "
        "clip-path 38.0 1395.96875 winding move 44.75648880004883 "
        "1423.6993408203125 line 159.24351501464844 1423.6993408203125 "
        "close");
    Check(!values.empty(), "values are read");
    Check(values.background_color->at(0) == 0.5198276042938232,
          "a color component is read exactly");
    Check(values.background_color_text->at(1) == "0.0",
          "the color's text is the recording's");
    Check(values.background_color->at(3) == 1.0, "the alpha");
    const auto& path = *values.clip_path;
    Check(path.origin_x == 38.0 && path.origin_y == 1395.96875, "the origin");
    Check(path.fill_type == "winding", "the fill type");
    Check(path.segments.size() == 3, "three verbs");
    Check(path.segments[0].verb == RecreationPathVerb::kMove, "a move");
    Check(path.segments[0].points.size() == 2 &&
              path.segments[0].points[1] == 1423.6993408203125,
          "a move's point");
    Check(path.segments[2].verb == RecreationPathVerb::kClose &&
              path.segments[2].points.empty(),
          "a close takes no point");
  }
  {
    const auto values = Parse(
        "clip-path 487.0 1396.84375 even-odd move 605.677978515625 "
        "1444.84375 conic 605.677978515625 1499.5218505859375 551.0 "
        "1499.5218505859375 0.7071067690849304 quad 1 2 3 4 cubic 1 2 3 4 5 "
        "6 close");
    Check(!values.background_color, "no color");
    const auto& path = *values.clip_path;
    Check(path.fill_type == "even-odd", "another fill type");
    Check(path.segments[1].verb == RecreationPathVerb::kConic, "a conic");
    Check(path.segments[1].points.size() == 4, "a conic's two points");
    Check(path.segments[1].weight == 0.7071067690849304, "a conic's weight");
    Check(path.segments[2].points.size() == 4, "a quad's two points");
    Check(path.segments[3].points.size() == 6, "a cubic's three points");
  }
  // Malformed text gives nothing.
  for (const char* text : {
           "background-color 1 0 0",
           "background-color 1 0 0 1 1",
           "background-color 1 0 0 x",
           "background-color 1 0 0 1; background-color 1 0 0 1",
           "background-color 1 0 0 1;clip-path 0 0 winding close",
           "clip-path 0 0 winding",
           "clip-path 0 0 nonzero close",
           "clip-path 0 winding close",
           "clip-path 0 0 winding move 1",
           "clip-path 0 0 winding conic 1 2 3 4",
           "clip-path 0 0 winding arc 1 2",
           "clip-path 0 0 winding move inf 1",
           "clip-path 0 0 winding  move 1 2",
           "clip-path 0 0 winding close; clip-path 0 0 winding close",
           "opacity 1",
           "background-color 1 0 0 1; ",
       }) {
    Check(Parse(text).empty(), text);
  }
  std::puts("recreation paint worklet values: all passed");
  return 0;
}
