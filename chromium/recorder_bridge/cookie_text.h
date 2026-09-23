#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_COOKIE_TEXT_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_COOKIE_TEXT_H_

#include <optional>
#include <string>
#include <string_view>
#include <vector>

// Reads cookie names and non-value attributes out of the cookie text Chromium
// hands to script and receives from script. Nothing here returns, stores, or
// logs a cookie value: every function reads the text once and keeps only the
// parts the recorder is allowed to record. The functions depend on the C++
// standard library alone so they can be exercised outside a Chromium build.
namespace a11y_recorder::cookie_text {

// The names in a document.cookie getter string, in the order Chromium
// serialized them. Chromium joins cookies with "; " and writes a cookie with an
// empty name as its value alone, so a segment without "=" is reported as a
// cookie with an empty name. A nameless cookie whose value itself holds "=" is
// indistinguishable from a named cookie in this text and is read as named.
std::vector<std::string> NamesFromCookieString(std::string_view cookie_string);

// The requested name and attributes of one document.cookie assignment or one
// unparsed Set-Cookie line. The value is skipped rather than copied.
struct CookieWriteText {
  std::string name;
  std::optional<std::string> domain;
  std::optional<std::string> path;
  std::optional<std::string> same_site;
  bool secure = false;
  bool http_only = false;
  bool partitioned = false;
  bool expires_present = false;
  bool max_age_present = false;
  // Every attribute name in the order written, lowercased, including ones the
  // recorder does not otherwise interpret.
  std::vector<std::string> attribute_names;
};

// Parses a cookie assignment the way RFC 6265bis section 5.6 reads a
// Set-Cookie line: the name-value pair ends at the first ";", the name ends at
// the first "=", a pair without "=" has an empty name, and attribute names are
// case-insensitive. The last Domain, Path, and SameSite attribute wins.
CookieWriteText ParseCookieWrite(std::string_view cookie_line);

// The parts of Chromium's CookieInclusionStatus debug string the recorder
// reports. The reason tokens are Chromium's own names, recorded as written.
struct CookieInclusionText {
  bool included = false;
  std::vector<std::string> exclusion_reasons;
  std::vector<std::string> warning_reasons;
  std::optional<std::string> exemption_reason;
};

CookieInclusionText ParseInclusionDebugString(std::string_view debug_string);

}  // namespace a11y_recorder::cookie_text

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_COOKIE_TEXT_H_
