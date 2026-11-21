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

Test 58 has bounded Windows/phone playback acceptance from the owner's
`h1.PNG`: Flutter, four of four APKs read, the matching native library
entry and the qualification about this screen are displayed in a
normal-weight wrapping row. This accepts the displayed metadata for
that recorded app only, not every capture or installer step. React
Native and old-recording target-machine acceptance remain unverified.
Tests 57 to 59 in `docs/validation/android-checks-plan.md` keep
installation, collection and old-recording behaviour separate.

## Native accessibility preflight measurement

This is evidence from a standalone synthetic native Views screen, not
recorder behaviour. The APK resource table and layout were read with
[AAPT2](https://developer.android.com/tools/aapt2). An authorized USB
phone run measured the synthetic screen through Android's public
accessibility nodes with no model: API 37, 390 dpi logical density,
and font scale 1.0. The test did not change settings or inspect any
other app.

| Synthetic resource ID | Package or fixture declaration | Live node size and unit | Live field evidence |
| --- | --- | --- | --- |
| `probe_heading` | `18sp` and Carlito Regular in a compiled style | 44 px, `sp` | Not a field |
| `xml_label` | `14dp`, `serif`, and XML `labelFor` | 34 px, `dp` | Not a field |
| `code_label` | `21sp`, monospace, and `setLabelFor` in Java; only its ID is present in the resource table | 51.1875 px, `sp` | Not a field |
| `xml_field` | XML email field with a separate hint and email autofill declaration | 44 px, `sp` | Editable; labelled by `xml_label`; hint showing |
| `code_field` | Java-created postal field with a separate hint | 44 px, `sp` | Editable; labelled by `code_label`; hint showing |
| `hint_field` | XML field with a hint and no separate label | 44 px, `sp` | Editable; hint showing; no label relationship returned |

All six nodes advertised the rendering extra. The pixel values and
units returned by Android matched the fixture's in-process `TextView`
values. The unit is reported separately and does not make the pixel
number a value in `sp` or `dp`
([rendering information API](https://developer.android.com/reference/kotlin/android/view/accessibility/AccessibilityNodeInfo.ExtraRenderingInfo)).
Android reported both label links separately from the hints
([accessibility node API](https://developer.android.com/reference/kotlin/android/view/accessibility/AccessibilityNodeInfo)).
An autofill declaration is not evidence of a runtime accessible label.

The APK bundled Carlito Regular and Caladea Italic. The heading style
references Carlito; the presence of Caladea is not evidence of use.
Matching the three expected typefaces via the fixture's own `TextView`
objects is ground truth available inside this test only, not something
the recorder obtained via accessibility. The rendering extra exposes
no font-family getter. This one font scale does not establish resizing
behaviour, and this run says nothing about coverage of other Android
versions, Compose, WebView or other custom or virtual nodes.

The synthetic APK was uninstalled and the temporary APK and build
files removed. No changes to recording, the companion or the fixture
app were made. No rule or preflight checkbox was implemented.

### Installed companion comparison

An unchanged v1.1.0 companion (protocol 8) returned the same six
resource IDs through its normal `accessibility-trees` request on the
physical phone at API 37, 390 dpi and font scale 1.0. The active,
focused synthetic window was not truncated and had no missing
children. The companion returned both explicit label links and
reported all three editable fields' hints separately. The hint-only
field had no label link, and none of the three fields had an
out-of-window label.

The companion did not return text size, text-size unit or typeface
for any of the six nodes. The earlier instrumentation run obtained
size and unit from Android at the same settings; their absence here
is a gap in the protocol 8 export, not in the synthetic nodes. The
label relationships do not need to be collected again. No APK or
raw tree was retained, and no recorder or phone settings were changed.

### One-shot text sizing in protocol 9

On its existing ADB-only socket the companion accepts
`{"type":"text-rendering","packageName":"org.example.synthetic"}`.
The reply is a `text-rendering` message addressed to the requester
only. It samples the selected app's active accessibility window, not
all open windows or all screens of the app. An inactive package
returns no nodes, and a change of active window during the read
discards the result. Collection is limited to 512 node lookups and a
two-second traversal budget checked between steps, not a hard timeout
on individual Android calls. An incomplete traversal says `partial`.

Each node has a snapshot index, optional resource ID, Android's
`visibleToUser` flag and an availability status. Pixel size and
Android text-size unit are separate fields when returned by the
[rendering information API](https://developer.android.com/reference/kotlin/android/view/accessibility/AccessibilityNodeInfo.ExtraRenderingInfo).
An unavailable size is not zero; an unavailable unit is not guessed.
The response contains no node text, field values, font families,
or copies of APKs. The recorder does not request sizes as part of
its normal capture streams. A rebuilt recorder requires the matching
protocol 9 companion.

On the physical phone, a temporary instrumentation harness ran the
production protocol handler and size collector with Android supplying
the active nodes. At API 37, 390 dpi and font scale 1.0, all six
pixel sizes and unit codes matched both a direct Android query and
the table above. Android marked the heading and XML label not
visible, but returned their sizes. The protocol kept those readings
with `visibleToUser: false`; it did not infer that the nodes were on
screen. Five non-text nodes returned no text size. The inactive and
invalid package requests returned no node data, and a normal clock
request still worked. This did not install or validate the release
companion service or change the installed one. The test APK, build
files and temporary ADB forward were removed.

### Recording dialog and playback

In Android mode, **Start recording** opens the naming dialog with
**Visible Android app**, **Refresh visible apps**, and **Collect app
accessibility evidence before recording**. The picker uses the
[Android visible-activity diagnostic](https://android.googlesource.com/platform/frameworks/base/+/master/services/core/java/com/android/server/wm/ActivityTaskManagerService.java),
filtered on the phone to activity package lines. It is not an installed,
recent or background app inventory. Only the optional snapshot uses
the selection; it does not launch the app, filter the phone recording
or save the list. The option starts unchecked each time.

With the option checked, the recorder inventories the selected app's
installed APKs as described below, then opens a separate protocol 9
connection and makes one live request before starting the session clock
or collectors. The selected app must
be the active accessibility window, including when more than one app
is visible. A refusal or timeout keeps the dialog open. Bring the app
to the foreground and retry, or clear the option to start without a
snapshot. A partial traversal is labelled rather than presented as
complete.

The optional `androidPreRecordingTextSizes` manifest member contains
the selected package, host UTC reply time, completion status and size
evidence. No snapshot member is written with the option off; older
recordings open without it. **Checks > Findings** shows the open
recording's snapshot above the app and visit list, even if the selected
package has no recorded visits. It shows the package, number of nodes
with a size, any partial status, and the host UTC reply time. Expand
**Node details** for wrapping, selectable node IDs, pixel sizes, unit
codes, visibility and missing data. If there is no snapshot, the page
says so for a phone recording.

A reply with no size values is still a saved snapshot. Checks says
`No text sizes available` and distinguishes nodes that did not advertise
size data from nodes whose size could not be read. It never substitutes a
zero size, a guessed font or a value from another screen. Android documents
this size and unit extra for `TextView`
([Android rendering extra](https://developer.android.com/reference/android/view/accessibility/AccessibilityNodeInfo#EXTRA_DATA_RENDERING_INFO_KEY)).
Flutter's Android bridge can represent Flutter semantics as virtual views
that look like Android controls
([Flutter AccessibilityBridge](https://api.flutter.dev/javadoc/io/flutter/view/AccessibilityBridge.html)).
A bundled Flutter marker does not establish that Flutter made every node
in the snapshot, or by itself explain the availability of any individual
reading.

The snapshot is evidence from one active window before that recording,
not a reading at the playhead or on later screens. Viewing it does not
seek or query the phone. Switching recordings replaces the evidence
and closes the details. Flutter and other framework evidence remains
under each app. There is no separate menu or viewer, and existing
recordings with a saved snapshot need no conversion.

The live size snapshot stores no node text, form values, APK files or
runtime font families. It does not run an accessibility rule or use a model. Windows
playback showed the evidence in the intended place for a saved snapshot
with no measured sizes. That confirms playback of the empty result,
not text-size collection on that screen. The corrected Node details
contrast still needs a Windows visual check.

### App-wide APK evidence and recorded forms

The opt-in read pulls only the selected package's installed base and
split APKs. It runs all applicable readers on each APK without choosing
one framework for the package. Each APK is deleted after reading,
including on error or cancellation. The readers open archive entries
in memory and do not execute app code or create extracted files. If
cleanup fails, recording preflight refuses to start. If the installed
version or last update time cannot be verified across the read, the
inventory is discarded. Library identification at recording stop
remains separate and unchanged.

The existing `androidPreRecordingTextSizes.apkFonts` member retains its
timestamp, version, split counts, font inventory and Flutter
`FontManifest.json` declarations. Its optional `accessibility` member
uses schema 1, with items identified by kind, scope, topic, split
basename, locator and value. The sibling coverage list reports what
each reader read, did not decode, did not identify or could not read
fully. Older font-only snapshots stay readable. The Checks detail panel
wraps and permits selection and Ctrl+C.

| Reader | Retained evidence | Limit |
| --- | --- | --- |
| ZIP and Flutter font declarations | Font paths and declarations of family, asset, weight and style; framework and bundle markers | Bundling does not prove use. Flutter AOT styles, scaling, forms, timers and semantic call paths are not decoded. |
| Android compiled XML and resource tables | Allowlisted manifest/layout/font attributes, explicit label declarations, direct Android style entries and their configuration variants, plus directly referenced fonts, dimensions, IDs and accessibility strings | Resource IDs remain explicit. It does not calculate inherited themes, arbitrary reference chains or the device's effective resources. |
| Android DEX | Allowlisted View, Compose semantics and timing API symbols | It does not establish a call, its arguments, runtime use, a route, or the behaviour of a timer. |
| React Native bundle | Bundle location and, when recognized, Hermes header signature | Bundled JavaScript and Hermes declarations and calls are not decoded. |
| Local HTML and SVG | Allowlisted semantics, component/form markup, label associations, and numeric HTML refresh declarations | No scripts, network, CSS cascade, live DOM, entered values or redirect URL. |

The compiled resource structures follow the [Android resource format](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/libs/androidfw/include/androidfw/ResourceTypes.h).
Readers cap document sizes, item sizes and counts and disclose incomplete
coverage. A component locator means the source declaration is known,
not that it appeared on any screen or that it was the effective value.
Compiled symbols are shown apart from declarations and observed evidence.

The pinned app-wide section distinguishes this inventory from any
dated APK or source review. To add an existing review, use **Attach
app review** in **Checks > Findings**. The selected JSON file must
match a package, version name and version code saved at the stop of
the open recording. The recorder saves the review as a sidecar inside
that recording, not globally, and does not overwrite an existing one.
The review date remains the original date. The schema uses
`schemaVersion: 1`, `package`, `versionName`, `versionCode`,
`reviewedUtc`, `method`, and an `observations` array with `topic`,
`evidence` and `limit` for each entry. The input and saved sidecar
contain reviewed evidence only, never the APK.

The same pinned area shows **Form evidence from recorded trees** for
settled app-window trees with visible editable nodes. The evidence
identifies the screen, visit and tree capture time, and shows label
links separately from hints, including links outside the captured
nodes. It also lists published input type, required and invalid
states and whether descriptions or error text existed. It does not
repeat entered field values, assume adjacent text is a label, or
interpret missing evidence as a conformance result.

The attached review and recorded tree can supply additional independent
evidence, without turning either into a per-screen style measurement.
No APK-derived item is assigned to a recorded screen or visit by this
inventory. No new conformance rule or model is run.
