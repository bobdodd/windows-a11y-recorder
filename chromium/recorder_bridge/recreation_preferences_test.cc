// Checks the reading of the recorded preferences a recreation gives its
// page, without a Chromium build. The test suite in chromium/test_integrate.py
// compiles and runs this file when a C++ compiler is available.

#include "chromium/recorder_bridge/recreation_preferences.h"

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

a11y_recorder::RecreationPreferences Parse(std::string_view text) {
  return a11y_recorder::ParseRecreationPreferences(text, ParseNumber);
}

// Stands in for color::mojom::RendererColorId, with one value more.
#define A11Y_RECORDER_FAKE_COLOR_ID(name) name,
enum class FakeColorId {
  A11Y_RECORDER_RENDERER_COLOR_IDS(A11Y_RECORDER_FAKE_COLOR_ID) kNotListed,
};
#undef A11Y_RECORDER_FAKE_COLOR_ID

}  // namespace

int main() {
  {
    const auto values = Parse(
        "field defaultFontSize i 20; field prefersReducedMotion b true; "
        "field caretBlinkIntervalMilliseconds n 530.5; "
        "field standardFontFamily t Times%20New%20Roman; "
        "field preferredColorScheme t dark; field mathFontFamily t ; "
        "zoom 1.2239010857415449; layoutZoom 2; "
        "color light kColorCssSystemWindow FFFFFFFF; "
        "color forcedColors kColorCssSystemWindow FF000000; "
        "color forcedColors kColorCssSystemWindowText FFFFFF00");
    Check(!values.empty(), "a full text gives values");
    Check(values.Integer("defaultFontSize") == 20, "an integer field");
    Check(values.Boolean("prefersReducedMotion") == true, "a boolean field");
    Check(values.Number("caretBlinkIntervalMilliseconds") == 530.5,
          "a number field");
    Check(values.Text("standardFontFamily") == "Times New Roman",
          "a text field is decoded");
    Check(values.Text("mathFontFamily") == "", "an empty text field");
    Check(values.Text("preferredColorScheme") == "dark", "a plain text field");
    Check(!values.Integer("prefersReducedMotion"),
          "a field is read only by its own type");
    Check(!values.Find("minimumFontSize"), "a field not given is absent");
    Check(values.zoom_level && *values.zoom_level > 1.22 &&
              *values.zoom_level < 1.23,
          "the zoom level");
    Check(values.layout_zoom_factor == 2.0, "the layout zoom factor");
    Check(values.color_maps.size() == 2, "two maps");
    Check(values.color_maps.at("light").at("kColorCssSystemWindow") ==
              0xFFFFFFFFu,
          "a light color");
    Check(values.color_maps.at("forcedColors").at("kColorCssSystemWindowText") ==
              0xFFFFFF00u,
          "a forced color");
  }
  Check(Parse("").empty(), "empty text gives nothing");
  Check(Parse("field defaultFontSize i 20.5").empty(),
        "a fractional integer gives nothing");
  Check(Parse("field defaultFontSize i 20; field defaultFontSize i 21").empty(),
        "a repeated field gives nothing");
  Check(Parse("zoom 1; zoom 2").empty(), "a repeated zoom gives nothing");
  Check(!Parse("layoutZoom 1.5").empty() &&
            Parse("layoutZoom 1.5").layout_zoom_factor == 1.5,
        "a layout zoom alone gives values");
  Check(Parse("layoutZoom 1; layoutZoom 2").empty(),
        "a repeated layout zoom gives nothing");
  Check(Parse("layoutZoom 0").empty(), "a layout zoom of 0 gives nothing");
  Check(Parse("layoutZoom -1").empty(),
        "a negative layout zoom gives nothing");
  Check(Parse("zoom nan").empty(), "a number that is not finite gives nothing");
  Check(Parse("field prefersReducedMotion b yes").empty(),
        "a boolean that is not true or false gives nothing");
  Check(Parse("field standardFontFamily t Times New").empty(),
        "an unencoded space gives nothing");
  Check(Parse("field standardFontFamily t Times%2").empty(),
        "a short escape gives nothing");
  Check(Parse("field standardFontFamily t a;b").empty(),
        "an unencoded reserved character gives nothing");
  Check(Parse("field default-size i 1").empty(),
        "a field name that is not letters gives nothing");
  Check(Parse("color sepia kColorCssSystemWindow FFFFFFFF").empty(),
        "an unknown map gives nothing");
  Check(Parse("color light kColorCssSystemWindow FFFFFF").empty(),
        "a short color gives nothing");
  Check(Parse("color light kColorCssSystemWindow FFFFFFFF; "
              "color light kColorCssSystemWindow FF000000")
            .empty(),
        "a repeated color gives nothing");
  Check(Parse("palette light").empty(), "an unknown key gives nothing");
  Check(Parse("zoom 1;").empty(), "a malformed separator gives nothing");

  Check(a11y_recorder::EncodeRecreationPreferenceText("Times New Roman") ==
            "Times%20New%20Roman",
        "a space is encoded");
  Check(a11y_recorder::EncodeRecreationPreferenceText("a;b%c") == "a%3Bb%25c",
        "reserved characters are encoded");
  {
    const std::string text = "Segoe UI; \xC3\xA9";
    const auto values = Parse(
        "field menuFontFamily t " +
        a11y_recorder::EncodeRecreationPreferenceText(text));
    Check(values.Text("menuFontFamily") == text,
          "encoded text, including UTF-8, reads back");
  }

  Check(std::string_view(a11y_recorder::RendererColorName(
            FakeColorId::kColorCssSystemWindow)) == "kColorCssSystemWindow",
        "a color's name");
  Check(a11y_recorder::RendererColorName(FakeColorId::kNotListed) == nullptr,
        "an unlisted color has no name");
  Check(a11y_recorder::RendererColorIdNamed<FakeColorId>(
            "kColorWebNativeControlSliderPressed") ==
            FakeColorId::kColorWebNativeControlSliderPressed,
        "a color by its name");
  Check(!a11y_recorder::RendererColorIdNamed<FakeColorId>("kNotListed"),
        "an unlisted name has no color");
  std::puts("recreation_preferences_test: all checks passed");
  return 0;
}
