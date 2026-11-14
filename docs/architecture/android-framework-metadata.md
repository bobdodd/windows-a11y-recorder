# Android app framework metadata

The recorder records recognised bundled frameworks for each Android app
seen in a recording. This is app metadata, not an accessibility check
or a claim that every screen uses that framework. The owner requested
framework names, when known, alongside the existing app information.

## Collection

At recording stop, after the accessibility tree collector has identified
the packages seen, `AndroidDeviceCollector` reads each installed app's
version as before. It then calls `AndroidFrameworkDetector` to:

- **Locate APKs:** run `adb -s <serial> shell pm path <package>`, including
  the base and installed splits. Duplicate paths are read once.
- **List entries:** run `adb -s <serial> shell unzip -l <path>` for each
  APK. Android's own tests document this listing format and verify that
  `-l` does not extract the named file
  ([AOSP unzip tests](https://android.googlesource.com/platform/external/toybox/+/5d35ee244cee3/tests/unzip.test)).
- **Match names:** recognise only exact entries under `lib/<ABI>/`,
  not text in diagnostics or a filename elsewhere in the archive.
- **Retain metadata only:** keep the supported framework names and the
  matched APK basename/entry names. Discard the full listing from
  memory after inspection. Nothing is pulled, extracted, decompiled,
  uploaded to a model, or written to a temporary APK directory.

Package names and APK paths are allowlisted before being passed to
`adb shell`; malformed or unsafe output is an unavailable read, not a
shell command. Each app's complete framework read has a 15-second
deadline. Failures preserve matches already found, report missing
coverage and degrade the device collector without discarding the
recording. The separate existing version read retains its own timeout.

## Supported markers

- **Flutter:** `lib/<ABI>/libflutter.so`. Flutter's documentation uses
  this library as its APK-identification marker
  ([Flutter Android deployment](https://docs.flutter.dev/deployment/android)).
- **React Native:** `lib/<ABI>/libreactnative.so` or the older
  `lib/<ABI>/libreactnativejni.so`. React Native documents its merged
  native library, and its migration discussion lists the previous
  libraries
  ([React Native release notes](https://reactnative.dev/blog/2024/10/23/release-0.76-new-architecture),
  [native library migration](https://github.com/react-native-community/discussions-and-proposals/discussions/816)).

Hermes, JSI, a JavaScript asset, a WebView or Flutter asset names alone
are not used as positive framework markers. A package can contain
multiple supported frameworks, so matches are not reduced to one
winner. No framework version, native Android implementation, Compose,
Cordova, Xamarin or .NET MAUI classification is inferred.

## Recording and playback

Each app in the existing `device.android` / `app-versions` event has an
optional `framework` object:

- **Source:** `apk-entry-names-v1`, the deterministic detector method.
- **Status:** `detected`, `unknown` when a listing was read but no
  supported marker matched, or `unavailable` when no listing was read.
- **Names and evidence:** sorted, deduplicated framework names and
  matching APK basename/entry names only.
- **Coverage:** APKs read and total APK paths returned, plus a problem
  when inspection was incomplete. A positive match may coexist with a
  problem; unknown with incomplete coverage is not a complete negative.

The parent event's read interval includes framework inspection, at the
stop rather than at each screen visit. These reads are not an atomic
snapshot of a package being updated concurrently. The recording
retains this metadata for offline playback; no reinspection is done
when an old recording opens.

The Checks list shows a wrapping row immediately beneath each app,
with its framework status, APK coverage, matched names and any problem.
Positive wording explicitly says that a bundled library was detected
at the stop, not that use by this screen was established. Older
recordings remain valid and show `Framework not recorded.` No manual
entry or editing flow is added.

## Validation

Synthetic on-phone listings test base/split coverage, both React Native
marker names, multiple frameworks, ambiguous names, malformed output,
shell-safe paths, partial failure and cancellation. Collector tests
exercise capture into `app-versions`; recording-codec, validator and
timeline tests exercise persistence, old records and playback text.
Tests do not use a client's APK or retain any extracted app files.

Phone verification and Windows visual acceptance are pending the
owner's next build. Tests 57 to 59 in
`docs/validation/android-checks-plan.md` cover installation, collection
and old-recording behaviour separately.
