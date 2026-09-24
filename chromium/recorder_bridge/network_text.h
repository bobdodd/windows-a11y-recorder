#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_NETWORK_TEXT_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_NETWORK_TEXT_H_

#include <cstddef>
#include <string>
#include <string_view>
#include <vector>

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

// The text recorded for a WebSocket message, an EventSource event, or a close
// reason. Parts that look like a credential are replaced by kWithheldMarker
// and the rest is kept, up to kMessageTextLimit UTF-16 code units.
inline constexpr std::string_view kWithheldMarker = "[withheld]";
inline constexpr size_t kMessageTextLimit = 4096;
// Only this many leading bytes are read. A credential that begins within the
// recorded text and ends within the scan is withheld whole.
inline constexpr size_t kMessageScanLimit = 65536;

// One withheld part. The offset is where its marker begins in the recorded
// text, in UTF-16 code units. The reason is kCredentialValue for a JSON Web
// Token or an HTTP authentication credential, and kCredentialName for the
// value of a field whose name suggests a credential.
struct WithheldText {
  size_t offset = 0;
  HeaderRedaction reason = HeaderRedaction::kNone;
};

struct MessageText {
  // UTF-8. Invalid UTF-8 is replaced by U+FFFD.
  std::string text;
  // True when the text is shorter than the message: the limit was reached or
  // the message is longer than the scan.
  bool truncated = false;
  std::vector<WithheldText> withheld;
};

// Reads the recordable text of one UTF-8 message. A withheld part is never
// copied into the result.
MessageText ReadMessageText(std::string_view utf8,
                            size_t limit = kMessageTextLimit);

}  // namespace a11y_recorder::network_text

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_NETWORK_TEXT_H_
