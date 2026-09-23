#include "chromium/recorder_bridge/cookie_text.h"

#include <cstddef>

namespace a11y_recorder::cookie_text {
namespace {

bool IsCookieWhitespace(char character) {
  return character == ' ' || character == '\t';
}

std::string_view Trim(std::string_view text) {
  while (!text.empty() && IsCookieWhitespace(text.front())) {
    text.remove_prefix(1);
  }
  while (!text.empty() && IsCookieWhitespace(text.back())) {
    text.remove_suffix(1);
  }
  return text;
}

std::string LowerAscii(std::string_view text) {
  std::string lowered(text);
  for (char& character : lowered) {
    if (character >= 'A' && character <= 'Z') {
      character = static_cast<char>(character - 'A' + 'a');
    }
  }
  return lowered;
}

bool StartsWith(std::string_view text, std::string_view prefix) {
  return text.substr(0, prefix.size()) == prefix;
}

// Returns the name part of one name-value pair. The value is never copied.
std::string NameOfPair(std::string_view pair) {
  const size_t equals = pair.find('=');
  if (equals == std::string_view::npos) {
    return std::string();
  }
  return std::string(Trim(pair.substr(0, equals)));
}

}  // namespace

std::vector<std::string> NamesFromCookieString(std::string_view cookie_string) {
  std::vector<std::string> names;
  if (Trim(cookie_string).empty()) {
    return names;
  }
  size_t start = 0;
  while (start <= cookie_string.size()) {
    size_t end = cookie_string.find(';', start);
    if (end == std::string_view::npos) {
      end = cookie_string.size();
    }
    const std::string_view pair = Trim(cookie_string.substr(start, end - start));
    if (!pair.empty()) {
      names.push_back(NameOfPair(pair));
    }
    start = end + 1;
  }
  return names;
}

CookieWriteText ParseCookieWrite(std::string_view cookie_line) {
  CookieWriteText result;
  size_t pair_end = cookie_line.find(';');
  if (pair_end == std::string_view::npos) {
    pair_end = cookie_line.size();
  }
  result.name = NameOfPair(Trim(cookie_line.substr(0, pair_end)));

  size_t start = pair_end + 1;
  while (start < cookie_line.size()) {
    size_t end = cookie_line.find(';', start);
    if (end == std::string_view::npos) {
      end = cookie_line.size();
    }
    const std::string_view attribute =
        Trim(cookie_line.substr(start, end - start));
    start = end + 1;
    if (attribute.empty()) {
      continue;
    }
    const size_t equals = attribute.find('=');
    const std::string name = LowerAscii(
        Trim(equals == std::string_view::npos ? attribute
                                              : attribute.substr(0, equals)));
    const std::string_view value =
        equals == std::string_view::npos ? std::string_view()
                                         : Trim(attribute.substr(equals + 1));
    result.attribute_names.push_back(name);
    if (name == "domain") {
      result.domain = std::string(value);
    } else if (name == "path") {
      result.path = std::string(value);
    } else if (name == "samesite") {
      result.same_site = std::string(value);
    } else if (name == "secure") {
      result.secure = true;
    } else if (name == "httponly") {
      result.http_only = true;
    } else if (name == "partitioned") {
      result.partitioned = true;
    } else if (name == "expires") {
      result.expires_present = true;
    } else if (name == "max-age") {
      result.max_age_present = true;
    }
  }
  return result;
}

CookieInclusionText ParseInclusionDebugString(std::string_view debug_string) {
  CookieInclusionText result;
  size_t start = 0;
  while (start < debug_string.size()) {
    size_t end = debug_string.find(',', start);
    if (end == std::string_view::npos) {
      end = debug_string.size();
    }
    const std::string_view token = Trim(debug_string.substr(start, end - start));
    start = end + 1;
    if (token.empty()) {
      continue;
    }
    if (token == "INCLUDE") {
      result.included = true;
    } else if (StartsWith(token, "EXCLUDE_")) {
      result.exclusion_reasons.emplace_back(token);
    } else if (StartsWith(token, "WARN_")) {
      result.warning_reasons.emplace_back(token);
    } else if (StartsWith(token, "Exemption")) {
      result.exemption_reason = std::string(token);
    }
    // DO_NOT_WARN and NO_EXEMPTION state the absence of warnings and of an
    // exemption, which the empty list and the null exemption already state.
  }
  return result;
}

}  // namespace a11y_recorder::cookie_text
