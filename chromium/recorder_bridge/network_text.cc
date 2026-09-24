#include "chromium/recorder_bridge/network_text.h"

#include <algorithm>
#include <array>
#include <cctype>
#include <string>
#include <string_view>
#include <vector>

namespace a11y_recorder::network_text {
namespace {

std::string Lowercase(std::string_view text) {
  std::string lowered;
  lowered.reserve(text.size());
  for (char character : text) {
    lowered.push_back(static_cast<char>(
        std::tolower(static_cast<unsigned char>(character))));
  }
  return lowered;
}

std::string_view TrimLeft(std::string_view text) {
  size_t start = 0;
  while (start < text.size() &&
         (text[start] == ' ' || text[start] == '\t')) {
    ++start;
  }
  return text.substr(start);
}

bool IsBase64UrlCharacter(char character) {
  return std::isalnum(static_cast<unsigned char>(character)) ||
         character == '-' || character == '_';
}

// Returns the length of a run of base64url characters starting at position.
size_t Base64UrlRun(std::string_view text, size_t position) {
  size_t end = position;
  while (end < text.size() && IsBase64UrlCharacter(text[end])) {
    ++end;
  }
  return end - position;
}

// A JSON Web Token is three base64url segments joined by dots, and its header
// segment encodes a JSON object, so it begins "eyJ". The signature segment may
// be empty for an unsecured token.
bool HoldsJsonWebToken(std::string_view value) {
  for (size_t start = value.find("eyJ"); start != std::string_view::npos;
       start = value.find("eyJ", start + 1)) {
    if (start > 0 && IsBase64UrlCharacter(value[start - 1])) {
      continue;
    }
    const size_t header = Base64UrlRun(value, start);
    size_t position = start + header;
    if (header < 4 || position >= value.size() || value[position] != '.') {
      continue;
    }
    const size_t payload = Base64UrlRun(value, position + 1);
    position += 1 + payload;
    if (payload == 0 || position >= value.size() || value[position] != '.') {
      continue;
    }
    return true;
  }
  return false;
}

bool BeginsWithAuthenticationScheme(std::string_view value) {
  static constexpr std::array<std::string_view, 7> kSchemes = {
      "basic ", "bearer ", "digest ", "negotiate ", "ntlm ", "hoba ",
      "mutual "};
  const std::string lowered = Lowercase(TrimLeft(value));
  for (std::string_view scheme : kSchemes) {
    if (lowered.starts_with(scheme)) {
      return true;
    }
  }
  return false;
}

// The words whose presence in a header or field name marks its value as a
// credential.
constexpr std::array<std::string_view, 9> kCredentialWords = {
    "token", "secret", "key",       "password", "session",
    "csrf",  "xsrf",   "signature", "auth"};

bool HoldsCredentialWord(std::string_view lowered_name) {
  for (std::string_view word : kCredentialWords) {
    if (lowered_name.find(word) != std::string_view::npos) {
      return true;
    }
  }
  return false;
}

// A part of a message to withhold, as byte positions [start, end).
struct Span {
  size_t start = 0;
  size_t end = 0;
  HeaderRedaction reason = HeaderRedaction::kNone;
};

bool IsWordCharacter(char character) {
  return std::isalnum(static_cast<unsigned char>(character)) ||
         character == '_';
}

bool IsNameCharacter(char character) {
  return IsWordCharacter(character) || character == '-' || character == '.';
}

// The token68 characters of an HTTP authentication credential.
bool IsToken68Character(char character) {
  return std::isalnum(static_cast<unsigned char>(character)) ||
         character == '-' || character == '.' || character == '_' ||
         character == '~' || character == '+' || character == '/' ||
         character == '=';
}

void FindJsonWebTokens(std::string_view text, std::vector<Span>& spans) {
  size_t start = text.find("eyJ");
  while (start != std::string_view::npos) {
    size_t next = start + 1;
    if (start == 0 || !IsBase64UrlCharacter(text[start - 1])) {
      const size_t header = Base64UrlRun(text, start);
      size_t position = start + header;
      if (header >= 4 && position < text.size() && text[position] == '.') {
        const size_t payload = Base64UrlRun(text, position + 1);
        position += 1 + payload;
        if (payload > 0 && position < text.size() && text[position] == '.') {
          position += 1 + Base64UrlRun(text, position + 1);
          spans.push_back({start, position, HeaderRedaction::kCredentialValue});
          next = position;
        }
      }
    }
    start = text.find("eyJ", next);
  }
}

// An authentication scheme name followed by a credential. The credential must
// be at least eight token68 characters and hold a digit, a punctuation
// character, or an upper-case letter after its first character, so that prose
// such as "basic authentication" is kept.
void FindAuthenticationCredentials(std::string_view text,
                                   std::vector<Span>& spans) {
  static constexpr std::array<std::string_view, 6> kSchemes = {
      "basic", "bearer", "negotiate", "ntlm", "hoba", "mutual"};
  const std::string lowered = Lowercase(text);
  for (std::string_view scheme : kSchemes) {
    for (size_t start = lowered.find(scheme); start != std::string::npos;
         start = lowered.find(scheme, start + 1)) {
      if (start > 0 && IsWordCharacter(text[start - 1])) {
        continue;
      }
      size_t position = start + scheme.size();
      const size_t spaces_start = position;
      while (position < text.size() &&
             (text[position] == ' ' || text[position] == '\t')) {
        ++position;
      }
      if (position == spaces_start) {
        continue;
      }
      const size_t credential_start = position;
      bool distinctive = false;
      while (position < text.size() && IsToken68Character(text[position])) {
        const char character = text[position];
        if (!std::isalpha(static_cast<unsigned char>(character)) ||
            (position > credential_start &&
             std::isupper(static_cast<unsigned char>(character)))) {
          distinctive = true;
        }
        ++position;
      }
      if (position - credential_start >= 8 && distinctive) {
        spans.push_back(
            {credential_start, position, HeaderRedaction::kCredentialValue});
      }
    }
  }
}

bool IsBareValueEnd(char character) {
  return std::isspace(static_cast<unsigned char>(character)) ||
         character == '&' || character == ',' || character == ';' ||
         character == '}' || character == ']' || character == ')' ||
         character == '"' || character == '\'' || character == '<' ||
         character == '>';
}

// The value of a field whose name suggests a credential, written as JSON
// ("name": "value"), a query or form pair (name=value), or a colon pair
// (name: value). A quoted value is withheld inside its quotes. An object or
// array value is left for its own fields, and true, false, and null are kept.
void FindCredentialNamedValues(std::string_view text,
                               std::vector<Span>& spans) {
  size_t position = 0;
  while (position < text.size()) {
    if (!IsNameCharacter(text[position]) ||
        (position > 0 && IsNameCharacter(text[position - 1]))) {
      ++position;
      continue;
    }
    const size_t name_start = position;
    while (position < text.size() && IsNameCharacter(text[position])) {
      ++position;
    }
    const size_t name_end = position;
    if (!HoldsCredentialWord(
            Lowercase(text.substr(name_start, name_end - name_start)))) {
      continue;
    }
    size_t cursor = name_end;
    if (name_start > 0 && cursor < text.size() &&
        (text[name_start - 1] == '"' || text[name_start - 1] == '\'') &&
        text[cursor] == text[name_start - 1]) {
      ++cursor;
    }
    while (cursor < text.size() && (text[cursor] == ' ' || text[cursor] == '\t')) {
      ++cursor;
    }
    if (cursor >= text.size() || (text[cursor] != ':' && text[cursor] != '=')) {
      continue;
    }
    ++cursor;
    while (cursor < text.size() && (text[cursor] == ' ' || text[cursor] == '\t')) {
      ++cursor;
    }
    if (cursor >= text.size()) {
      continue;
    }
    const char first = text[cursor];
    if (first == '{' || first == '[') {
      continue;
    }
    size_t value_start = cursor;
    size_t value_end = cursor;
    if (first == '"' || first == '\'') {
      value_start = cursor + 1;
      value_end = value_start;
      while (value_end < text.size() && text[value_end] != first) {
        value_end += text[value_end] == '\\' ? 2 : 1;
      }
      value_end = std::min(value_end, text.size());
    } else {
      while (value_end < text.size() && !IsBareValueEnd(text[value_end])) {
        ++value_end;
      }
      const std::string_view bare =
          text.substr(value_start, value_end - value_start);
      if (bare == "true" || bare == "false" || bare == "null") {
        position = value_end;
        continue;
      }
    }
    if (value_end > value_start) {
      spans.push_back(
          {value_start, value_end, HeaderRedaction::kCredentialName});
    }
    position = std::max(position, value_end);
  }
}

// Returns the length of the UTF-8 sequence at position, or 0 when the bytes
// there are not a complete, valid sequence.
size_t Utf8SequenceLength(std::string_view text, size_t position) {
  const unsigned char lead = static_cast<unsigned char>(text[position]);
  size_t length = 0;
  unsigned int minimum = 0;
  unsigned int code_point = 0;
  if (lead < 0x80) {
    return 1;
  } else if ((lead & 0xE0) == 0xC0) {
    length = 2;
    minimum = 0x80;
    code_point = lead & 0x1F;
  } else if ((lead & 0xF0) == 0xE0) {
    length = 3;
    minimum = 0x800;
    code_point = lead & 0x0F;
  } else if ((lead & 0xF8) == 0xF0) {
    length = 4;
    minimum = 0x10000;
    code_point = lead & 0x07;
  } else {
    return 0;
  }
  if (position + length > text.size()) {
    return 0;
  }
  for (size_t index = 1; index < length; ++index) {
    const unsigned char continuation =
        static_cast<unsigned char>(text[position + index]);
    if ((continuation & 0xC0) != 0x80) {
      return 0;
    }
    code_point = (code_point << 6) | (continuation & 0x3F);
  }
  if (code_point < minimum || code_point > 0x10FFFF ||
      (code_point >= 0xD800 && code_point <= 0xDFFF)) {
    return 0;
  }
  return length;
}

}  // namespace

HeaderRedaction ClassifyHeader(std::string_view name, std::string_view value) {
  const std::string lowered = Lowercase(name);
  static constexpr std::array<std::string_view, 5> kCredentialHeaders = {
      "cookie", "set-cookie", "set-cookie2", "authorization",
      "proxy-authorization"};
  for (std::string_view header : kCredentialHeaders) {
    if (lowered == header) {
      return HeaderRedaction::kCredentialHeader;
    }
  }
  // These names hold a credential word but carry the host or an
  // authentication challenge rather than a credential.
  static constexpr std::array<std::string_view, 3> kExemptNames = {
      ":authority", "www-authenticate", "proxy-authenticate"};
  bool exempt = false;
  for (std::string_view header : kExemptNames) {
    if (lowered == header) {
      exempt = true;
    }
  }
  if (!exempt) {
    if (HoldsCredentialWord(lowered)) {
      return HeaderRedaction::kCredentialName;
    }
  }
  if (BeginsWithAuthenticationScheme(value) || HoldsJsonWebToken(value)) {
    return HeaderRedaction::kCredentialValue;
  }
  return HeaderRedaction::kNone;
}

const char* HeaderRedactionName(HeaderRedaction redaction) {
  switch (redaction) {
    case HeaderRedaction::kNone:
      return "";
    case HeaderRedaction::kCredentialHeader:
      return "credential-header";
    case HeaderRedaction::kCredentialName:
      return "credential-name";
    case HeaderRedaction::kCredentialValue:
      return "credential-value";
  }
  return "";
}

MessageText ReadMessageText(std::string_view utf8, size_t limit) {
  MessageText result;
  const std::string_view scan = utf8.substr(0, kMessageScanLimit);
  std::vector<Span> spans;
  FindJsonWebTokens(scan, spans);
  FindAuthenticationCredentials(scan, spans);
  FindCredentialNamedValues(scan, spans);
  std::sort(spans.begin(), spans.end(), [](const Span& a, const Span& b) {
    return a.start < b.start || (a.start == b.start && a.end > b.end);
  });
  std::vector<Span> merged;
  for (const Span& span : spans) {
    if (!merged.empty() && span.start < merged.back().end) {
      merged.back().end = std::max(merged.back().end, span.end);
    } else {
      merged.push_back(span);
    }
  }

  size_t units = 0;
  size_t position = 0;
  size_t next_span = 0;
  while (position < scan.size()) {
    if (next_span < merged.size() && merged[next_span].start == position) {
      if (units + kWithheldMarker.size() > limit) {
        result.truncated = true;
        return result;
      }
      result.withheld.push_back({units, merged[next_span].reason});
      result.text.append(kWithheldMarker);
      units += kWithheldMarker.size();
      position = merged[next_span].end;
      ++next_span;
      continue;
    }
    const size_t length = Utf8SequenceLength(scan, position);
    if (length == 0 && scan.size() < utf8.size() &&
        scan.size() - position < 4) {
      // The scan ended inside a character of a longer message.
      break;
    }
    const size_t character_units = length == 4 ? 2 : 1;
    if (units + character_units > limit) {
      result.truncated = true;
      return result;
    }
    if (length == 0) {
      result.text.append("\xEF\xBF\xBD");
      position += 1;
    } else {
      result.text.append(scan.substr(position, length));
      position += length;
    }
    units += character_units;
  }
  result.truncated = scan.size() < utf8.size();
  return result;
}

}  // namespace a11y_recorder::network_text
