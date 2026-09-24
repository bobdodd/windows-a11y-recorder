// Checks the header value classifier and the message text reader without a
// Chromium build. The test suite
// in chromium/test_integrate.py compiles and runs this file when a C++
// compiler is available, since the classifier depends on the standard library
// alone.

#include "chromium/recorder_bridge/network_text.h"

#include <cstdio>
#include <cstring>
#include <string>

namespace {

int failures = 0;

void Expect(bool condition, const char* description) {
  if (!condition) {
    std::fprintf(stderr, "FAILED: %s\n", description);
    ++failures;
  }
}

using a11y_recorder::network_text::ClassifyHeader;
using a11y_recorder::network_text::HeaderRedaction;
using a11y_recorder::network_text::HeaderRedactionName;
using a11y_recorder::network_text::kWithheldMarker;
using a11y_recorder::network_text::MessageText;
using a11y_recorder::network_text::ReadMessageText;

void TestCredentialHeaders() {
  for (const char* name : {"Cookie", "set-cookie", "SET-COOKIE2",
                           "Authorization", "Proxy-Authorization"}) {
    Expect(ClassifyHeader(name, "anything") ==
               HeaderRedaction::kCredentialHeader,
           name);
  }
}

void TestCredentialNames() {
  for (const char* name :
       {"X-Api-Key", "x-csrf-token", "X-XSRF-TOKEN", "x-session-id",
        "X-Amz-Signature", "x-client-secret", "X-Password", "x-auth-user",
        "Sec-WebSocket-Key"}) {
    Expect(ClassifyHeader(name, "plain") == HeaderRedaction::kCredentialName,
           name);
  }
  for (const char* name : {":authority", "WWW-Authenticate",
                           "Proxy-Authenticate"}) {
    Expect(ClassifyHeader(name, "example.test") == HeaderRedaction::kNone,
           name);
  }
}

void TestCredentialValues() {
  Expect(ClassifyHeader("X-Forwarded", "Bearer abc") ==
             HeaderRedaction::kCredentialValue,
         "bearer scheme");
  Expect(ClassifyHeader("X-Forwarded", "  basic dXNlcjpwYXNz") ==
             HeaderRedaction::kCredentialValue,
         "basic scheme with leading space, any case");
  Expect(ClassifyHeader("X-Trace",
                        "v=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2ln") ==
             HeaderRedaction::kCredentialValue,
         "embedded JSON Web Token");
  Expect(ClassifyHeader("X-Trace", "eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIn0.") ==
             HeaderRedaction::kCredentialValue,
         "unsecured JSON Web Token with empty signature");
  Expect(ClassifyHeader("X-Trace", "keyJabc.def.ghi") == HeaderRedaction::kNone,
         "eyJ inside a longer word is not a token start");
  Expect(ClassifyHeader("X-Trace", "eyJhbGci") == HeaderRedaction::kNone,
         "a lone segment is not a token");
}

void TestOrdinaryHeaders() {
  for (const char* name : {"Content-Type", "Accept", "Cache-Control",
                           "Location", "Keep-Alive", ":path", "Referer"}) {
    Expect(ClassifyHeader(name, "text/html") == HeaderRedaction::kNone, name);
  }
  Expect(ClassifyHeader("Accept", "basically anything") ==
             HeaderRedaction::kNone,
         "a word starting with a scheme name is not a scheme");
}

void TestNames() {
  Expect(std::strcmp(HeaderRedactionName(HeaderRedaction::kNone), "") == 0,
         "no reason for kNone");
  Expect(std::strcmp(HeaderRedactionName(HeaderRedaction::kCredentialHeader),
                     "credential-header") == 0,
         "credential-header name");
  Expect(std::strcmp(HeaderRedactionName(HeaderRedaction::kCredentialName),
                     "credential-name") == 0,
         "credential-name name");
  Expect(std::strcmp(HeaderRedactionName(HeaderRedaction::kCredentialValue),
                     "credential-value") == 0,
         "credential-value name");
}


void TestMessageTextKeepsOrdinaryText() {
  const MessageText plain = ReadMessageText("{\"type\":\"chat\",\"text\":\"hi\"}");
  Expect(plain.text == "{\"type\":\"chat\",\"text\":\"hi\"}", "plain JSON kept");
  Expect(!plain.truncated, "plain JSON not truncated");
  Expect(plain.withheld.empty(), "plain JSON has nothing withheld");
  const MessageText prose =
      ReadMessageText("basic authentication is described; bearer tokens too");
  Expect(prose.withheld.empty(), "prose after a scheme name is kept");
  const MessageText literals =
      ReadMessageText("{\"sessionActive\":true,\"token\":null}");
  Expect(literals.withheld.empty(), "true, false, and null are kept");
  const MessageText object =
      ReadMessageText("{\"auth\":{\"user\":\"a\"}}");
  Expect(object.withheld.empty(), "an object value is left to its fields");
}

void TestMessageTextWithholdsMatchedParts() {
  const MessageText json =
      ReadMessageText("{\"op\":\"login\",\"access_token\":\"abc123\",\"n\":1}");
  Expect(json.text == "{\"op\":\"login\",\"access_token\":\"[withheld]\",\"n\":1}",
         "JSON credential value withheld inside its quotes");
  Expect(json.withheld.size() == 1 && json.withheld[0].offset == 30 &&
             json.withheld[0].reason == HeaderRedaction::kCredentialName,
         "JSON credential offset and reason");
  const MessageText query = ReadMessageText("a=1&api_key=XYZ987&b=2");
  Expect(query.text == "a=1&api_key=[withheld]&b=2", "query pair withheld");
  const MessageText colon = ReadMessageText("Session: 42 remaining");
  Expect(colon.text == "Session: [withheld] remaining", "colon pair withheld");
  const MessageText escaped =
      ReadMessageText("{\"password\":\"a\\\"b\",\"x\":1}");
  Expect(escaped.text == "{\"password\":\"[withheld]\",\"x\":1}",
         "an escaped quote does not end a quoted value");
  const MessageText bearer =
      ReadMessageText("AUTH Bearer AbCdEf123456.xyz ok");
  Expect(bearer.text == "AUTH Bearer [withheld] ok", "bearer credential");
  Expect(bearer.withheld.size() == 1 &&
             bearer.withheld[0].reason == HeaderRedaction::kCredentialValue,
         "bearer credential reason");
  const MessageText jwt = ReadMessageText(
      "id eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2ln end");
  Expect(jwt.text == "id [withheld] end", "JSON Web Token withheld");
  const MessageText both = ReadMessageText(
      "{\"token\":\"eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2ln\"}");
  Expect(both.text == "{\"token\":\"[withheld]\"}" && both.withheld.size() == 1,
         "overlapping matches withhold once");
}

void TestMessageTextLimits() {
  const MessageText cut = ReadMessageText(std::string(10, 'a'), 4);
  Expect(cut.text == "aaaa" && cut.truncated, "cut at the limit");
  const MessageText marker = ReadMessageText("ab token=secretvalue", 12);
  Expect(marker.text == "ab token=" && marker.truncated &&
             marker.withheld.empty(),
         "a marker that would pass the limit is dropped");
  // U+1F600 is one character and two UTF-16 code units.
  const MessageText pair = ReadMessageText("a\xF0\x9F\x98\x80z", 2);
  Expect(pair.text == "a" && pair.truncated,
         "a surrogate pair is not split");
  const MessageText units = ReadMessageText("\xF0\x9F\x98\x80 key=v");
  Expect(units.withheld.size() == 1 && units.withheld[0].offset == 7,
         "offsets count UTF-16 code units");
  const MessageText invalid = ReadMessageText("a\xFFz");
  Expect(invalid.text == "a\xEF\xBF\xBDz", "invalid UTF-8 is replaced");
  const std::string long_message(
      a11y_recorder::network_text::kMessageScanLimit + 10, 'b');
  const MessageText scan = ReadMessageText(long_message, 1u << 20);
  Expect(scan.truncated &&
             scan.text.size() == a11y_recorder::network_text::kMessageScanLimit,
         "a message longer than the scan is truncated at the scan");
  Expect(kWithheldMarker == "[withheld]", "marker text");
}

}  // namespace

int main() {
  TestCredentialHeaders();
  TestCredentialNames();
  TestCredentialValues();
  TestOrdinaryHeaders();
  TestNames();
  TestMessageTextKeepsOrdinaryText();
  TestMessageTextWithholdsMatchedParts();
  TestMessageTextLimits();
  if (failures != 0) {
    std::fprintf(stderr, "%d failure(s)\n", failures);
    return 1;
  }
  return 0;
}
