// Checks the header value classifier without a Chromium build. The test suite
// in chromium/test_integrate.py compiles and runs this file when a C++
// compiler is available, since the classifier depends on the standard library
// alone.

#include "chromium/recorder_bridge/network_text.h"

#include <cstdio>
#include <cstring>

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

}  // namespace

int main() {
  TestCredentialHeaders();
  TestCredentialNames();
  TestCredentialValues();
  TestOrdinaryHeaders();
  TestNames();
  if (failures != 0) {
    std::fprintf(stderr, "%d failure(s)\n", failures);
    return 1;
  }
  return 0;
}
