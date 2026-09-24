#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_NETWORK_TEXT_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_NETWORK_TEXT_H_

#include <string>
#include <string_view>

// Decides which HTTP header values the recorder may keep. Every header name is
// recorded. A value is withheld when the header is one that carries a cookie
// or a credential by definition, when its name suggests a credential, or when
// the value itself has the shape of a credential. A withheld value is never
// copied, returned, or logged; only the reason it was withheld is. The
// functions depend on the C++ standard library alone so they can be exercised
// outside a Chromium build.
namespace a11y_recorder::network_text {

// Why a header value was withheld. kNone means the value may be recorded.
enum class HeaderRedaction {
  kNone,
  // Cookie, Set-Cookie, Set-Cookie2, Authorization, or Proxy-Authorization.
  kCredentialHeader,
  // A name holding token, secret, key, password, session, csrf, xsrf,
  // signature, or auth, compared without regard to case.
  kCredentialName,
  // A value that begins with an HTTP authentication scheme or holds a JSON Web
  // Token.
  kCredentialValue,
};

// Returns why the value of one header is withheld, or kNone.
HeaderRedaction ClassifyHeader(std::string_view name, std::string_view value);

// The recorded name of a redaction reason, or an empty string for kNone.
const char* HeaderRedactionName(HeaderRedaction redaction);

}  // namespace a11y_recorder::network_text

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_NETWORK_TEXT_H_
