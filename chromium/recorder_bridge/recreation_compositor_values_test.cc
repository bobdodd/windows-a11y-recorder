// Checks the reading of the compositor values a recreation imposes, without
// a Chromium build. The test suite in chromium/test_integrate.py compiles and
// runs this file when a C++ compiler is available.

#include "chromium/recorder_bridge/recreation_compositor_values.h"

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

a11y_recorder::RecreationCompositorValues Parse(std::string_view text) {
  return a11y_recorder::ParseRecreationCompositorValues(text, ParseNumber);
}

}  // namespace

int main() {
  const std::string identity = " 1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1";
  {
    const auto values = Parse("");
    Check(values.empty(), "no text is no values");
  }
  {
    const auto values = Parse(
        "rotate-transform 0.7071067811865476 -0.7071067811865475 0 0 "
        "0.7071067811865475 0.7071067811865476 0 0 0 0 1 0 0 0 0 1; "
        "translate-transform 1 0 0 30 0 1 0 0 0 0 1 0 0 0 0 1; "
        "opacity 0.800000011920929; filter blur 1.5, grayscale 0.25; "
        "backdrop-filter drop-shadow 2 3 -4 0 0 0 0.5");
    Check(!values.empty(), "values are read");
    Check(values.transforms.size() == 2, "two transforms");
    Check(values.transforms.at("rotate-transform")[0] == 0.7071067811865476,
          "a matrix entry is read exactly");
    Check(values.transforms.at("rotate-transform")[1] == -0.7071067811865475,
          "a negative entry");
    Check(values.transforms.at("translate-transform")[3] == 30,
          "entries are row by row");
    Check(values.opacity == 0.800000011920929, "the opacity");
    Check(values.opacity_text == std::string("0.800000011920929"),
          "the opacity's text is kept");
    Check(static_cast<float>(*values.opacity) == 0.8f,
          "the opacity reads back as the recorded float");
    Check(values.filter && values.filter->size() == 2, "two filters");
    Check((*values.filter)[0].type == "blur" &&
              (*values.filter)[0].numbers == std::vector<double>{1.5},
          "a blur");
    Check((*values.filter)[1].type == "grayscale", "a grayscale");
    Check(values.backdrop_filter && values.backdrop_filter->size() == 1 &&
              (*values.backdrop_filter)[0].numbers.size() == 7,
          "a drop shadow's seven numbers");
  }
  {
    const auto values = Parse("filter; opacity 1e-05");
    Check(values.filter && values.filter->empty(), "an empty filter list");
    Check(values.opacity == 1e-05, "an exponent");
  }
  {
    const auto values = Parse("backdrop-filter reference");
    Check(values.backdrop_filter && values.backdrop_filter->size() == 1 &&
              (*values.backdrop_filter)[0].numbers.empty(),
          "an operation with no numbers");
  }
  // Malformed text gives no values at all.
  const char* malformed[] = {
      "opacity",
      "opacity ",
      "opacity x",
      "opacity 0.5; opacity 0.5",
      "opacity nan",
      "opacity inf",
      "opacity 0.5;opacity 0.5",
      "opacity  0.5",
      "colour 1",
      "primary-transform 1 0 0",
      "filter Blur 1",
      "filter blur 1,grayscale 1",
      "filter blur 1; filter blur 2",
      "; opacity 0.5",
  };
  for (const char* text : malformed) {
    if (!Parse(text).empty()) {
      std::fprintf(stderr, "failed: malformed text is read: %s\n", text);
      return 1;
    }
  }
  Check(Parse("primary-transform" + identity + "; primary-transform" +
              identity)
            .empty(),
        "a repeated transform");
  Check(!Parse("scale-transform" + identity).empty(), "a scale transform");
  Check(Parse("offset-transform" + identity).empty(), "an unknown namespace");
  std::puts("recreation compositor values: all passed");
  return 0;
}
