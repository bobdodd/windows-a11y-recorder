#include "chromium/recorder_bridge/network_text.h"

#include <array>
#include <cctype>
#include <string>
#include <string_view>

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
    static constexpr std::array<std::string_view, 9> kCredentialWords = {
        "token", "secret", "key",       "password", "session",
        "csrf",  "xsrf",   "signature", "auth"};
    for (std::string_view word : kCredentialWords) {
      if (lowered.find(word) != std::string::npos) {
        return HeaderRedaction::kCredentialName;
      }
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

}  // namespace a11y_recorder::network_text
