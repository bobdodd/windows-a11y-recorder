// Checks the cookie text readers without a Chromium build. The test suite in
// chromium/test_integrate.py compiles and runs this file when a C++ compiler is
// available, since the readers depend on the standard library alone.

#include "chromium/recorder_bridge/cookie_text.h"

#include <cstdio>
#include <string>
#include <vector>

namespace {

int failures = 0;

void Expect(bool condition, const char* description) {
  if (!condition) {
    std::fprintf(stderr, "FAILED: %s\n", description);
    ++failures;
  }
}

using a11y_recorder::cookie_text::NamesFromCookieString;
using a11y_recorder::cookie_text::ParseCookieWrite;
using a11y_recorder::cookie_text::ParseInclusionDebugString;

void TestNames() {
  Expect(NamesFromCookieString("").empty(), "empty cookie string has no names");
  Expect(NamesFromCookieString("   ").empty(), "blank cookie string has no names");
  Expect((NamesFromCookieString("a=1; b=2") ==
          std::vector<std::string>{"a", "b"}),
         "names in order");
  Expect((NamesFromCookieString("a=x=y; nameless") ==
          std::vector<std::string>{"a", ""}),
         "value with equals keeps name; segment without equals is nameless");
  Expect((NamesFromCookieString(" spaced = v ;") ==
          std::vector<std::string>{"spaced"}),
         "names are trimmed and empty trailing segments are skipped");
  for (const std::string& name : NamesFromCookieString("secret=hunter2")) {
    Expect(name.find("hunter2") == std::string::npos, "value never returned");
  }
}

void TestWrite() {
  auto write = ParseCookieWrite(
      "theme=dark; Path=/; SameSite=Lax; Secure; Max-Age=60; Foo=bar");
  Expect(write.name == "theme", "write name");
  Expect(write.path && *write.path == "/", "write path");
  Expect(write.same_site && *write.same_site == "Lax", "write samesite raw");
  Expect(!write.domain, "absent domain");
  Expect(write.secure, "secure");
  Expect(!write.http_only, "httponly absent");
  Expect(write.max_age_present && !write.expires_present, "max-age only");
  Expect((write.attribute_names ==
          std::vector<std::string>{"path", "samesite", "secure", "max-age",
                                   "foo"}),
         "attribute names lowercased in order");

  auto nameless = ParseCookieWrite("onlyvalue; Domain=example.test");
  Expect(nameless.name.empty(), "pair without equals is nameless");
  Expect(nameless.domain && *nameless.domain == "example.test", "domain");

  auto last_wins = ParseCookieWrite("a=b; Path=/x; path=/y; HTTPONLY; Partitioned; Expires=Wed");
  Expect(last_wins.path && *last_wins.path == "/y", "last path wins");
  Expect(last_wins.http_only && last_wins.partitioned && last_wins.expires_present,
         "flags case-insensitive");
}

void TestInclusion() {
  auto included = ParseInclusionDebugString("INCLUDE, DO_NOT_WARN, NO_EXEMPTION");
  Expect(included.included, "included");
  Expect(included.exclusion_reasons.empty() && included.warning_reasons.empty(),
         "no reasons");
  Expect(!included.exemption_reason, "no exemption");

  auto excluded = ParseInclusionDebugString(
      "EXCLUDE_USER_PREFERENCES, EXCLUDE_THIRD_PARTY_PHASEOUT, "
      "WARN_THIRD_PARTY_PHASEOUT, ExemptionTopLevelStorageAccess");
  Expect(!excluded.included, "excluded");
  Expect((excluded.exclusion_reasons ==
          std::vector<std::string>{"EXCLUDE_USER_PREFERENCES",
                                   "EXCLUDE_THIRD_PARTY_PHASEOUT"}),
         "exclusion reasons");
  Expect((excluded.warning_reasons ==
          std::vector<std::string>{"WARN_THIRD_PARTY_PHASEOUT"}),
         "warning reasons");
  Expect(excluded.exemption_reason &&
             *excluded.exemption_reason == "ExemptionTopLevelStorageAccess",
         "exemption");
}

}  // namespace

int main() {
  TestNames();
  TestWrite();
  TestInclusion();
  if (failures == 0) {
    std::puts("cookie_text: all checks passed");
  }
  return failures == 0 ? 0 : 1;
}
