#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_INPUT_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_INPUT_H_

#include <array>
#include <string_view>

// Which pages a recreation's input refusal leaves alone ("Input refused only
// in the recreation" in docs/architecture/page-recreation.md). A browser
// page, one whose URL scheme is one of these, takes input as in any
// Chromium: the DevTools front end, the evidence panel inside it, and the
// browser's own pages. It uses the C++ standard library alone, so it can be
// exercised outside a Chromium build.
namespace a11y_recorder {

inline constexpr std::array<std::string_view, 4> kBrowserPageSchemes = {
    "devtools", "chrome", "chrome-untrusted", "chrome-extension"};

// True for a browser page's URL scheme, given as Blink's KURL holds it, in
// lower case and without the colon.
inline bool IsBrowserPageScheme(std::string_view scheme) {
  for (const std::string_view browser_scheme : kBrowserPageSchemes) {
    if (scheme == browser_scheme) {
      return true;
    }
  }
  return false;
}

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_INPUT_H_
