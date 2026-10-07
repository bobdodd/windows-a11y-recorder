// Checks which pages a recreation's input refusal leaves alone, without a
// Chromium build. The test suite in chromium/test_integrate.py compiles and
// runs this file when a C++ compiler is available.

#include "chromium/recorder_bridge/recreation_input.h"

#include <cstdio>

namespace {

int failures = 0;

void Expect(bool condition, const char* description) {
  if (!condition) {
    std::fprintf(stderr, "FAILED: %s\n", description);
    ++failures;
  }
}

}  // namespace

int main() {
  using a11y_recorder::IsBrowserPageScheme;
  Expect(IsBrowserPageScheme("devtools"), "the DevTools front end");
  Expect(IsBrowserPageScheme("chrome-extension"), "the evidence panel");
  Expect(IsBrowserPageScheme("chrome"), "a browser page");
  Expect(IsBrowserPageScheme("chrome-untrusted"), "an untrusted browser page");
  Expect(!IsBrowserPageScheme("http"), "a recreation served on loopback");
  Expect(!IsBrowserPageScheme("https"), "a recreation at its recorded address");
  Expect(!IsBrowserPageScheme("about"), "about:blank and about:srcdoc");
  Expect(!IsBrowserPageScheme("file"), "a recorded file page");
  Expect(!IsBrowserPageScheme(""), "no scheme");
  Expect(!IsBrowserPageScheme("chrome-extension-x"), "only whole schemes");
  Expect(!IsBrowserPageScheme("DEVTOOLS"), "schemes as KURL holds them");
  if (failures == 0) {
    std::printf("recreation input tests passed\n");
  }
  return failures == 0 ? 0 : 1;
}
