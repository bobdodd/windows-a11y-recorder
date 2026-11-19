# Android check fixtures

## Status

Planned on 2026-11-18 at the owner's request, and the first app begun.
The owner asked for "comprehensive fully automated fixtures to test the
checks we have built and the ones left to build. Especially the ones
left to build as we don't have recordings with appropriate data. We
will need to test for both issue found and false positives", made
partly interactively from an Android app the owner navigates while
recording. The owner then accepted the recommendations below: "I'll
accept your recommendations. Write the fixture plan and then start
building the Android test app (or apps, I guess we might need more than
one). Please remember though that the unbuilt checks are the priority
once the test framework is proven".

What exists so far:

- this plan;
- the first fixture app, `android/fixtures/`, Views and WebView, with
  the five proof screens (see "The proof screens");
- its build, `scripts/Build-FixtureApp.ps1`, which runs the companion
  app's pinned build with `-App fixtures`;
- the tour, `scripts/Run-FixtureTour.ps1`, which opens each screen in
  turn while the recorder records;
- the expected answers, `android/fixtures/expected.json`, and a test
  that keeps them in step with the app's source and the checks' names;
- the extractor, `tests/FixtureExtract`, run on the PC by
  `scripts/Extract-FixtureRecording.ps1`, and the rule tests,
  `tests/Recorder.Tests/AndroidFixtureRecordingTests.cs` (see "Rule
  tests").

Not yet built: the model evaluation, and most of the fixtures for the
checks not yet built. The first screens for them, "Recipes"
(`colour-only`) and "Kitchen help" (`link-purpose`), are built and wait
for their first recordings (see "The colour screen" and "The link
purpose screen").

One fixture recording is committed, `tests/fixtures/android/tour/`,
extracted from the owner's fifth recording of the tour on 2026-11-19
with the phone's serial and other apps' settings replaced (see "The
fifth recording"). The earlier recordings were measured in the sandbox
and not kept.

## Owner's decisions

Accepted on 2026-11-18, as quoted above.

1. **The expected answers live in the app's source, not in the
   recordings.** Each check gets a screen, or part of one, that fails,
   and a near miss that is correct, placed to tempt a false alarm.
   Every element a check is expected to judge has a stable resource
   ID. The expected answers list, for each such element, the expected
   finding and its level, or none. The tests score each check: found,
   missed, and false alarm.
2. **Most navigation is scripted.** The owner presses Record in the
   recorder; a script on the PC opens each screen over adb (`am start`
   with the screen's name as an extra) and holds it a few seconds. The
   recorder has no command to start a recording, so Record stays a
   person's step. A person drives only where a person matters:
   TalkBack navigation, typing, timing and gestures, and later the
   keyboard. A recording can be made again whenever the app changes.
3. **Two kinds of test.**
   - **Rule tests, in the repository.** Compact fixtures are taken from
     the recordings: each visit's tree, a settled screenshot and OCR's
     lines. Their content is made up for the app, so they hold no
     client data. They run in every build and must match the expected
     answers exactly.
   - **A model evaluation, run only on request.** A probe-style report
     for each model check of what was found, missed and falsely
     alarmed, reusing kept answers, so a run with nothing changed asks
     nothing again.
4. **Real recordings stay in use.** The fixture app measures how well a
   check finds faults made on purpose; the twenty real recordings
   remain the test of false alarms in real apps.
5. **Scope.**
   - **Frameworks:** Views and WebView first, built by the companion
     app's pinned build with no Gradle. Compose and Flutter versions
     come later, as separate apps, each with its own pinned toolchain.
   - **Storage:** full recordings of the tour stay off the repository,
     on the PC or as a release download; only the compact fixtures are
     committed.
   - **Order:** a few checks already built first, to prove the
     framework; then fixtures for the checks not yet built, which are
     the priority and also give those checks their "measure first"
     data.

## The apps

### The Views and WebView app

- **Source:** `android/fixtures/`, plain Java against the platform's
  `android.jar`, with no AndroidX or other libraries, as the companion
  app is.
- **Package:** `io.github.bobdodd.a11yrecorder.fixtures`, shown as "A11y
  Fixtures". It is for testing only and is never published.
- **Build:** `scripts/Build-FixtureApp.ps1`, which runs
  `scripts/Build-CompanionApp.ps1 -App fixtures`. One script keeps one
  set of pinned downloads (Temurin JDK 21.0.13+9, Build-Tools 37 and
  the API 37 platform). The fixture app adds two steps the companion
  app does not need: `aapt2 link --java` to make `R.java` for its
  resource IDs, and `-A assets` for the WebView's pages. It has its own
  signing key, in
  `%LOCALAPPDATA%\Windows A11y Recorder Build\fixtures-signing`, and is
  written to `artifacts\fixture-app\output\a11yrecorder-fixtures.apk`.
- **Screens:** one activity, `FixtureActivity`. With no extra it shows
  an index of every screen, as buttons, for a person to choose from;
  with `--es screen <id>` it shows that screen. The screen's title is
  the activity's title, and each screen starts with one heading, so a
  screen passes the title and heading checks unless it is there to
  test them.
- **Stable IDs:** every element named in the expected answers has a
  resource ID declared in `res/values/ids.xml`, so its finding is
  reported as `id:io.github.bobdodd.a11yrecorder.fixtures:id/<name>`.
- **Made-up content:** every word, picture and colour is invented for
  the app. No client app's content, and no recording's text, is copied
  into a screen.

### Later apps

Each later app has its own folder, package, build and expected answers,
and shares the tour script and the extractor.

- **Compose:** needs Kotlin and the Compose compiler, which the
  companion app's build does not have; a separate pinned toolchain.
- **Flutter:** needs the Flutter SDK; its semantics tree reaches the
  accessibility tree differently from Views, which is the point of
  testing it.

## The expected answers

`android/fixtures/expected.json` lists the screens and, for each,
what each check should find. A field this plan does not name is an
error, so the file cannot drift into meaning more than the tests read.

```json
{
  "version": 1,
  "package": "io.github.bobdodd.a11yrecorder.fixtures",
  "auditLevel": "AA",
  "app": [
    { "check": "no-live-region", "by": "rule", "finding": "warning",
      "note": "the app has no live region" }
  ],
  "screens": [
    {
      "id": "touch-targets",
      "title": "Basket",
      "framework": "views",
      "how": "tour",
      "seconds": 5,
      "stage": "proof",
      "expect": [
        { "check": "touch-target-24dp", "item": "touch_pair_add", "by": "rule", "finding": "error" },
        { "check": "touch-target-24dp", "item": "touch_spaced", "by": "rule", "finding": "none",
          "note": "under 24dp, but its 24dp circle meets no other target" }
      ]
    }
  ]
}
```

- **app:** the app-level findings expected, those about the whole app
  rather than a screen, such as `no-live-region`: each has `check`,
  `by`, `finding` and `note`, as a screen's expectations do, and no
  `item` or `line`.
- **id:** the screen's name, as passed to `--es screen`.
- **title:** the activity's title, which the recorder uses to tell
  screens apart. It describes the screen's made-up content, such as
  "Basket", not the check, so the title checks have nothing to find.
- **framework:** `views` or `webview`.
- **how:** `tour`, opened by the tour script; `person`, which also
  has `steps`, the steps a person takes on the screen, in at most five;
  or `start`, the app's index, which the tour starts on and opens again
  after the person presses Record. Its title is the app's name, and it
  has no `steps`.
- **seconds:** how long the tour holds a `tour` or `start` screen.
- **stage:** `proof`, a built check proving the framework; `unbuilt`, a
  check not yet built, or built with its screen's recording not yet
  extracted and committed, as every other tour screen must be in a
  recording; or `built`, a built check's later coverage.
- **expect:** one entry for each check and element.
  - **check:** a check's name as the recorder reports it, such as
    `touch-target-24dp`; a test checks every name against the
    recorder's list of checks. A check not yet built has a name chosen
    here, marked by `"by": "unbuilt"`, until it is built.
  - **item:** the element's resource ID name, or `null` for a finding
    about the whole screen, such as `no-heading`. In a WebView it is
    the HTML element's `id`, which WebView is expected to report as the
    item's resource name; to be measured on the first recording.
  - **line:** in place of `item`, the text of an OCR line, for the
    checks that judge OCR's lines rather than the tree's items, such as
    text contrast.
  - **by:** `rule`, decided without a model; `model`, decided by a
    model's answer; `unbuilt`, not yet a check.
  - **finding:** `error`, `warning`, `info`, `not-run`, or `none` for a
    near miss that must have no finding of this check.
  - **note:** why the element is built as it is.

Findings not listed are unexpected. Each is reviewed when its screen is
first recorded: either the screen is changed so the finding goes, or
the finding is added to the expected answers with a note saying why it
is right. After that review, an unlisted error or warning fails the
rule tests, so a change to a check that adds false alarms is caught;
an unlisted information note or check not run is only reported (the
owner's decision of 2026-11-19, below).

For a model check, the rule tests can only test the part decided
without a model: that the element is asked about, or left out, and
with what. The answer itself is scored by the model evaluation.

## Recording the tour

`scripts/Run-FixtureTour.ps1`:

1. finds the recorder's own adb, in
   `%LOCALAPPDATA%\Windows A11y Recorder\android\platform-tools`, so the
   tour talks to the recorder's adb server rather than starting one of
   another version, which would stop the server under a recording;
2. installs the fixture app;
3. checks that TalkBack is on (TalkBack among the enabled accessibility
   services and touch exploration on), and waits for the person to turn
   it on if it is not;
4. lists the other apps with notifications on the phone, read with
   `adb shell cmd notification list`, and asks the person to clear them,
   as demo mode did not hide their icons on the owner's phone (measured,
   2026-11-19). Enter checks again, and GO records with them. The
   system's, TalkBack's and the recorder's own apps are not listed;
5. puts the accessibility button in the navigation bar, setting
   `accessibility_button_mode` to 0 (1 is the floating menu, 2 the
   gesture), as the floating button was over tested lines in a different
   place on each recording;
6. puts the status bar in Android's demo mode: a fixed clock, a full
   battery and no notification icons. Network icons are not set, as the
   demo mode's network commands are reported to stop the System UI on
   recent Android;
7. opens the fixture app's index, so the recording starts in the
   fixture app rather than on the home screen;
8. asks the person to press Record in the recorder, and waits for
   Enter;
9. opens the index again, the `start` screen, so that it has a
   screenshot of its own in the recording, then each `tour` screen, each
   with `am start -W`, holds each for its `seconds`, and names each in
   the console as it goes;
10. asks the person to press Stop, waits for Enter, puts the
   accessibility button's setting back, and takes the status bar out of
   demo mode, as it does too if the tour stops early.

Screens marked `person` are not opened by the tour; the person opens
them from the app's index and follows their steps.

The tour is recorded with TalkBack on, as the owner's recordings of
real apps are, so that the fixtures are like the recordings the checks
meet. Whether TalkBack's focus outline changes any check's result on
the fixture screens is to be measured on the next recording; the first
was made with TalkBack off.

## Rule tests

Built on 2026-11-19 against the owner's first recording of the tour.

- **The extractor**, `tests/FixtureExtract`, runs on the PC, as the
  checks read Windows OCR, which does not run in the Linux build.
  `scripts/Extract-FixtureRecording.ps1` builds it and runs it on the
  newest recording, or the one named, writing
  `tests/fixtures/android/tour/`. It:
  1. trims the recording to the fixture app's visits, from the start of
     the first to the end of the last, into a new `recording.mcap`.
     Sound and video are left out, as are the phone's notification
     events, which name other apps and hold their text, and TalkBack's
     log, which no check reads: on the second recording it was 3,281 of
     the 4,469 events in the span, one line each in the list of text
     not the app's. What TalkBack said is kept, in its speech channel.
     The record of the apps seen in the recording, read when it stopped,
     keeps only the fixture app's entry, as the others name the phone's
     apps and their versions. The video frames the rules read in place of
     a screenshot are copied, as a frame is used only if its file is
     there when the recording is opened. As the repository is public,
     the phone's serial number is replaced by the stand-in
     `fixture-phone` in every event and in the recording's metadata, and
     any phone setting naming an app other than the fixture app, TalkBack
     or the recorder's companion app by `left out`; the extractor writes
     nothing if a kept event still names the serial (owner, 2026-11-19,
     see "The fifth recording"). A `manifest.json` lists only the kept
     files;
  2. runs the rules on the trimmed recording with Windows OCR and no
     model (every model check is marked not asked), keeping only the
     pictures the rules read, what OCR read in each (`ocr.json`), and
     the findings (`findings.json`);
  3. prints what it replaced and each visit's findings, then four lists
     headed READ BEFORE COMMITTING: the recording's metadata, the
     packages named in the kept events, the serial numbers and every
     phone setting kept, and every text kept that is not in the fixture
     app's source, so a person can see that nothing of the phone's own
     is kept before the folder is committed.

  This keeps the trimmed recording rather than separate trees,
  screenshots and lines for each visit, as first planned: the checks
  read a recording's visits and events through the same playback the
  recorder uses, so a trimmed recording tests that path too, with no
  second loader to keep in step. Measured on the first recording: 1,099
  events and five screenshots kept, 1.3 MB in all.
- **The rule tests**, `AndroidFixtureRecordingTests`, read every folder
  under `tests/fixtures/android/` that holds a `recording.mcap`, and
  are skipped while there is none. Each run gives the rules OCR's
  readings from `ocr.json`, so they run where Windows OCR does not.
  - The rules' findings must equal `findings.json`, the findings made
    on the PC: this shows the replay of OCR and the trimmed recording
    lose nothing the rules read.
  - Each `rule` expectation in `expected.json` is compared with the
    findings of its screen, matched by its title: an `item` matches a
    finding whose identity names that resource ID, and a `line` a
    finding whose label or identity holds that text. An expected
    finding missing, a finding at another level, or a finding on a near
    miss fails the test, naming the recording, screen, check and
    element. Every `tour` screen whose checks are built must be in some
    recording.
  - Each `app` expectation is compared in the same way with the
    fixture app's app-level findings, matched by check.
  - No fixture may name a phone serial number other than the stand-in,
    or hold a phone setting naming another app.
  - Any other error or warning on the fixture app, on a screen or the
    whole app, fails the test, naming it; any other information note or
    check not run is written to the test's output only.
  - A near miss whose check was not run, such as text not measured as
    something is drawn over it, fails as not judged, rather than as a
    false alarm.

## The first recording

Measured on 2026-11-19 in the sandbox, with no OCR and no model, from
the owner's first recording of the tour. The recording was not kept.

- **The proof checks found what they were built to find.** On the
  touch-target screen, `touch-target-24dp` found the close pair and not
  the spaced button, and `touch-target-size` found every button under
  48 dp and not the 48 dp one. On the contrast screen,
  `text-contrast-aa` gave an error at 2.17:1, a warning at 3.03:1 and
  nothing on the near miss. With the recording trimmed, the rule tests
  passed the replay test and failed the expected answers only where
  the two faults below predict.
- **The control-name check needs OCR.** It reads only snapshots OCR
  has read, so with no OCR it found nothing on the control-name screen.
  The extractor therefore runs with Windows OCR.
- **The WebView screen did not load its page.** WebView showed its
  own error page for `file:///android_asset/web/basics.html`, with
  `net::ERR_FILE_NOT_FOUND`, though the page is in the app; the cause
  is not known. The checks then reported that error page. The screen
  now reads the page from the app's assets itself and gives it to
  WebView with `loadDataWithBaseURL`, and shows a plain message naming
  the file if it cannot be read.
- **The recording held the phone's own content.** It began on the home
  screen, whose launcher showed the owner's location, and the status
  bar named other apps' notifications; a notification event held one
  app's text. The tour now starts in the fixture app's index and puts
  the status bar in demo mode, and the extractor leaves notification
  events out and lists any text left that is not the fixture app's.
- **TalkBack was off.** The recorded preferences showed touch
  exploration off, so the tour now checks for TalkBack before it
  starts.
- **Findings no expectation names.** Measured on the fixture app's
  screens: `non-text-contrast` information on every image button,
  measured from the tree as OCR was not used; `duplicate-speakable-text`
  information on each screen's heading, which repeats the app bar's
  title; and an app-level `no-live-region` warning. These are left for
  the owner's decision on unlisted findings.

## The second recording

Measured on 2026-11-19 in the sandbox, with no OCR and no model, from
the owner's second recording of the tour, made with TalkBack on and the
changed tour. The recording was not kept. On the PC the extractor ran
with Windows OCR; that run's findings have not been read.

- **The tour's changes worked.** The recording began in the fixture
  app's index; the status bar showed the demo clock and battery and no
  notifications; no notification event was in the span. Apart from
  TalkBack's log, the text not the app's was the status and navigation
  bars, the accessibility button's window and the app's package
  details.
- **TalkBack's drawing changed the contrast results.** In the
  screenshots, TalkBack's green focus outline is drawn round the first
  line of text after each screen's heading, and the accessibility
  button, a purple circle at the left edge, covers the start of the
  second line. Measured on the items' bounds, with no OCR:
  `contrast_error` was measured on the outline's green, #6BC840, at
  2.11:1, an error still but for the wrong colour; and on the Points
  screen `heading_value`, dark text, gave a false error at 2.98:1, on
  the button's purple, #A28ACD. With TalkBack off, on the first
  recording, neither happened. The same will happen in recordings of
  real apps made with TalkBack on.
- **The WebView screen still had no page.** The screen's own message
  said the asset `web/basics.html` could not be read. The owner listed
  the PC's build of the app: it holds `assets/web\basics.html`, with a
  backslash, which Android does not find, where the sandbox's build
  holds `assets/web/basics.html` (measured). The backslash comes from
  aapt2's `-A` on Windows (inferred, as aapt2 writes the assets).
  `scripts/Build-CompanionApp.ps1` now gives every such name forward
  slashes and stops if one is left; this is likely also why the first
  recording's `file:///android_asset/` URL was not found (inferred).

## The owner's decisions on the second recording

Chosen on 2026-11-19, quoted as given: "1: B" and "2: I accept your
recommendation".

1. **What is drawn over the app is not measured (B).** The text and
   non-text contrast checks do not measure text or a control that
   TalkBack's focus outline or another window, such as the
   accessibility button, covers, and the finding, a check not run, says
   why. Built as follows.
   - **The focus outline** is found from the picture, round the item
     the tree marks as having accessibility focus: at the middle of each
     side, a run of one colour (within 48, summed over red, green and
     blue) from the item's edge inward, the same colour on all four
     sides, not the colour just outside, at least 4 of the screen's
     pixels and under a quarter of the item's size. Its thickness is the
     longest run. Text or a control is covered when its box meets the
     item's bounds and is not wholly inside the outline.
   - **A window above the app's** covers text or a control whose box
     meets the window's bounds, as the tree gives them. Words whose
     middle is under such a window are still left out as not the app's.
   - The finding says, for example, "not measured, as an outline,
     #6BC840, 10 pixels thick, is drawn over it, round it, the item with
     accessibility focus, as a screen reader shows its focus", or "not
     measured, as another window, untitled, TYPE_SYSTEM, is drawn over
     part of it". Covered text and controls are not counted as measured.
   - The outline is looked for round the item that has focus in the
     tree the visit's items were read from; if focus moved before the
     picture was taken, the outline is not found (inferred, not seen).
2. **Unlisted findings.** Errors and warnings must be listed in
   `expected.json`; information notes are only reported. App-level
   findings are listed in its `app` array.

Measured on the second recording, in the sandbox, with no OCR and no
model, after both were built:

- Every contrast finding on the outline's green and the button's purple
  is now a check not run, saying why: on each screen, the line with the
  outline, and on the Delivery and Points screens, the lines the
  accessibility button's window meets.
- On the Delivery screen this leaves none of its three lines measured:
  the error line has the outline, and the button's window, at the left
  edge from 543 to 661 pixels down, meets the warning line and the top
  of the near miss's bounds. The fixture's contrast answers cannot be
  confirmed from a recording made this way until the screen or the tour
  is changed so TalkBack covers none of its lines.
- The rule tests on it fail, as expected, on the Delivery lines, on
  `names_resource` (as OCR was not used) and on the WebView screen,
  whose page did not load, with an unlisted `no-heading` error.

Then chosen on 2026-11-19, quoted as given: "option A". The fixture
app's screens with tested text, Delivery, Points and the WebView page,
now have a line that is not tested after the heading, where TalkBack's
focus lands, and then a 120 dp gap before the tested lines. On the
owner's phone this puts the first tested line about 180 pixels below
the accessibility button's window (inferred from the second recording's
bounds, not yet recorded). The button can be dragged elsewhere; if it
then covers a tested line, the check says so and the rule test fails
on that line as not judged. Recordings of real apps are not changed.

## The third recording

Measured on 2026-11-19 from the owner's third recording of the tour,
made with TalkBack on and the fixture app with option A: the
extractor's output on the PC, with Windows OCR, and the recording's
events and screenshots in the sandbox. The recording was not kept.

- **The proof checks on Views found what they were built to find.** On
  the Delivery screen, `text-contrast-aa` gave an error at 2.17:1 and a
  warning at 3.03:1, and nothing on the near miss: the outline was on
  the line not tested, and the accessibility button's window met none
  of the tested lines. With OCR, `control-name-not-descriptive` found
  `names_resource`. The touch-target checks were as before.
- **The WebView page loaded, but under the app bar.** The tree gave the
  WebView the whole window, 0 to 2404 pixels down, and the page's
  heading 39 to 107 pixels down, under the status bar and the app bar,
  which end at 298; the heading was not visible to the user, and the
  line not tested was under the app bar too. The WebView had been given
  the padding that keeps the Views screens clear of the system bars,
  and did not move its page in by it. As a result:
  - the visit was split in two, named by different first texts, as the
    heading was visible on the first snapshot and not on the next, so
    the second visit was not matched to the "Opening hours" screen;
  - `first-heading-low` warned that the first heading, the `h2`, was
    260 dp down;
  - the tested lines were under the accessibility button's window, so
    `text-contrast-aa` did not measure them.

  The WebView is now held in a frame that takes the padding, as the
  Views screens' columns do. That this puts the page below the app bar
  is inferred, not yet recorded.
- **Findings no expectation names**, which would fail the rule tests:
  - on the index screen, `text-shown-as-heading` warned that the app
    bar's title, "A11y Fixtures", 46 pixels tall against 24 for body
    text, is not marked as a heading. The index was in the tour but not
    in `expected.json`; see the owner's decisions below.
  - on the index screen, `non-text-contrast` did not measure two of the
    screen buttons, under the accessibility button's window; a check
    not run is only reported.
- **The phone's own content was in the kept events.** In demo mode the
  status bar still showed two notification icons of another app, in
  the screenshots and in the tree, so the demo command that hides them
  did not hide them on this phone; and the record of the apps seen in
  the recording, read when it stopped, named another app and its
  version. The fixture from this recording is not to be committed.

### The owner's decisions on the third recording

The owner chose on 2026-11-19 ("1:A", "2: B", "3: yes"):

- **The index screen's app bar title:** the index screen is listed in
  `expected.json`, as the `start` screen, with this warning expected,
  as the check judges any app bar title alike; the index's own
  "Screens" heading is its near miss. The check is not changed.
- **Notification icons in the status bar:** the tour asks the owner to
  clear the phone's notifications before Record and checks that none
  of another app is left. The extractor does not change the status
  bar's window.
- **The record of the apps seen:** the extractor keeps only the fixture
  app's entry in it. Not yet run on a recording.

## The fourth recording

Measured on 2026-11-19 from the owner's fourth recording of the tour,
made with TalkBack on and the owner's decisions on the third recording
in place: the extractor's output on the PC, with Windows OCR, and the
recording's events, read here.

- **Worked:**
  - the WebView page was below the app bar: its heading 337 to 405
    pixels down, the app bar ending at 298. The page was one visit, with
    no `first-heading-low` warning;
  - Basket's touch targets, Holiday photos' `btn_close`, Delivery's
    warning at 3.03:1 and its near miss, and the WebView's error at
    2.17:1 were as expected;
  - the record of the apps seen kept only the fixture app's entry, and
    no other app's notification or text was in the kept events.
- **The index had no picture in the fixture.** Its only screenshot was
  taken 0.1 seconds before its first snapshot, so the rules matched the
  snapshot to the video frame before the next screen opened, at 7.81
  seconds. The extractor left out the video, and so the fixture had no
  picture of the index and no finding on it.
- **The accessibility button moved.** It was 543 to 661 pixels down on
  the third recording and 889 to 1007 on the fourth, at the left edge,
  over Delivery's error line and the WebView's near miss, which were not
  measured. A gap in the layout cannot follow it.

### The owner's decisions on the fourth recording

The owner chose on 2026-11-19 ("Problem 1: option C", "Problem 2:
option A"):

- **The index's picture (C, both):** the extractor copies the video
  frames the rules read in place of a screenshot, so the fixture gives
  the full recording's answers; and the tour opens the index again after
  Record and holds it for its `seconds`, so it has a screenshot of its
  own. The frame the rules read on the fourth recording was taken as
  the next screen opened, and may show that screen beginning to appear
  (inferred).
- **The accessibility button (A):** the tour puts it in the navigation
  bar and puts it back afterwards. That the setting moves the button on
  the owner's phone while TalkBack is on is not yet recorded.

## The fifth recording

Measured on 2026-11-19 from the owner's fifth recording of the tour,
made with TalkBack on and the owner's decisions on the fourth recording
in place: the extractor's output on the PC, with Windows OCR, and the
extracted folder, read here.

- **The rules gave every expected answer.** Both rule tests passed on
  the extracted folder in the Linux build: the rules found exactly what
  they found on the PC, every `rule` and `app` expectation was met, and
  no error or warning outside `expected.json` was found.
  - The index, visited at the start and again after Record, had its
    pictures, its expected heading warning on the app bar title and no
    finding on its near miss.
  - Delivery's near miss and the WebView's near miss were measured and
    gave no finding; no line was left unmeasured as covered.
- **The accessibility button was in the navigation bar.** The floating
  button's window was not in the recording, and the pictures show the
  button in the navigation bar, so the setting moved it on the owner's
  phone with TalkBack on.
- **The phone's own details were kept.** A search of every kept event
  found the phone's serial number in 26 events (the screenshots, the
  settings, the record of apps seen, the display density, the input
  devices and the companion app's connection) and, in the phone's
  settings, the package of another app installed on the phone as its
  preferred speech engine. The READ BEFORE COMMITTING lists had missed
  both, as they listed only the packages and text of the screens'
  events. The folder was not committed.

### The owner's decision on the fifth recording

The owner chose on 2026-11-19 ("Go with option A and re-extract the
recording with the serial and other app settings replaced"): the
extractor replaces the serial with a stand-in and the settings naming
another app with a marker, rather than removing them, as the recorder's
checks on stored events require every one of these events to have a
serial; and the READ BEFORE COMMITTING lists show every serial and
setting kept. Run here on the extracted folder, with no OCR, it replaced
the serial in 26 events and the speech engine in both settings events,
and no kept event or output file named the serial afterwards; the
rules' findings on the result equalled the PC's (measured).

The owner then re-extracted the recording on the PC with Windows OCR.
In the result, measured here: the stand-in serial is in 26 events and
`left out` in the 2 settings events; neither the serial nor the other
app's name is in any file; the pictures are byte for byte the ones
reviewed before; and the three rule tests pass on it. It was committed
as `tests/fixtures/android/tour/`.

The text list then read some of an item's text by names the companion
app does not write (`hintText` and `tooltipText`, where it writes
`hint` and `tooltip`), and left out its supplementary description, its
actions' labels and its row and column titles. Fixed on 2026-11-19: the
list reads those fields by the names the companion app writes, and each
link's address (step 29). Run here on the committed fixture with no
OCR, the list was the same before and after the fix, so that fixture
holds none of that text that is not in the fixture app's source
(measured).

## Model evaluation

Run only on request, on the PC, as the earlier probes were: the model
checks are queued on the tour's recording with the project's models,
and a report gives, for each model check, the faults found, the faults
missed, the false alarms, the answers that could not tell, and the
questions and tokens used. Kept answers are reused, so a second run with
nothing changed asks nothing again. Its report is evidence of what the
models did on made-up faults, not proof of accuracy on real apps.

## The proof screens

Checks already built, to prove the framework end to end. Each pairs
faults with near misses.

| Screen | Checks | Fails | Near miss |
| --- | --- | --- | --- |
| `touch-targets` | touch-target-size, touch-target-24dp | Two 20 dp buttons 4 dp apart; a 32 dp button | A 20 dp button with nothing within 24 dp (passes 2.5.8 by spacing, still under ATF's 48 dp); a 48 dp button |
| `text-contrast` | text-contrast-aa | 16 sp grey #B0B0B0 on white, about 2.2:1 (an error); #949494, about 3.0:1, which fails unless the text is large (a warning) | #767676 on white, about 4.54:1, just over 4.5:1 |
| `control-names` | control-name-not-descriptive | An icon-only button named with its resource name; an arrow button named "Arrow", which names the icon, not what it does | The same arrow named "Next photo" |
| `heading-look` | heading-not-shown | A value, "1,250 points", in body text, marked as a heading | "Points", large and bold, marked as a heading |
| `web-basics` | text-contrast-aa, heading-not-shown | Paragraph text at about 2.2:1; an `h2` styled as body text | Text at about 16:1; an `h1` shown as a heading |

The touch target fails are made so that each check has one plain
answer: the 20 dp pair fails both, the 32 dp button fails only ATF's
48 dp, and the 48 dp button passes both. The near miss for text
contrast passes from a screenshot; from the video the check allows for
the video's approximate colours and warns that it is near the limit, so
which picture the visit's line was measured from is recorded with the
fixture. AAA checks (touch-target-44dp,
text-contrast-aaa) are left to a run at the AAA audit level.

## The checks not yet built

The priority once the proof is through. Each gets a screen with
faults and near misses, and its first recording is the check's
"measure first" data: what the recorder holds for it, measured with no
model, before rules are proposed to the owner. The order is a proposal;
the owner chooses.

### Opened by the tour

| Check | WCAG | Fails | Near miss |
| --- | --- | --- | --- |
| An informative image with no description | 1.1.1 | A photo-like picture and a status icon with no description | A decorative divider image with no description; a described picture |
| Content in an order that does not make sense | 1.3.2 | Two columns read across rather than down; a caption read before what it captions | The same blocks in a sensible order |
| A graphic with no item in the tree | 1.1.1 | A drawn progress bar and a small bar chart with no item | A drawn decorative pattern with no item |
| The contrast of such a graphic | 1.4.11 | Chart bars at about 1.5:1 against the background | Bars at 3:1 or more |
| Text cut off or overlapping | 1.4.4, 1.4.10 | A label cut to one line with an ellipsis; two texts overlapping | Text that fits; text that wraps |
| A selected state shown only by colour | 1.4.1 | A row of tabs where only the colour of one differs | A row where the selected tab is also underlined and bold |
| Links shown only by colour | 1.4.1 | A clickable phrase in a paragraph, only its colour different | A clickable phrase that is also underlined |
| A chart that relies on colour | 1.4.1 | Two lines told apart only by colour | Two lines with different markers and labels |
| Instructions that rely on shape, colour or place | 1.3.3 | "Tap the green button on the right" | "Tap Continue" beside a green button |
| Video with no captions | 1.2.2 | A short made-up video with speech-like sound and no captions | The same video with captions shown |
| Flashing | 2.3.1 | See "Limits" | A slow pulse under three a second |

The owner chose to start with the selected state and the links shown
only by colour, on one new screen (2026-11-19); the chart that relies on
colour waits.

#### The colour screen

`colour-only`, titled "Recipes", built on 2026-11-19 and opened by the
tour. Its checks were built on 2026-11-19 (android-checks.md, step 29,
step 4), with the rules the owner chose, and their names in
`expected.json`, `selected-only-by-colour` and `link-only-by-colour`,
are the recorder's. Both are errors, as no model is involved (owner,
2026-11-19). The screen stays at the `unbuilt` stage until its
recording is extracted and committed.

| Element | Built as | Expected |
| --- | --- | --- |
| `selected_colour` | The selected tab of three, marked selected for accessibility services, in #B0306A against the others' #5F6368: both about 6:1 on white and of the same luminance, so only the hue differs | An error: a selected state shown only by colour |
| `selected_marked` | The selected tab of three, in the same colours, and bold and underlined too | No finding |
| `link_colour` | A paragraph holding a link in #1558B0, not underlined: about 2.3:1 against the #202124 body text, under the 3:1 a colour alone would need, and 6.9:1 on white | An error: a link shown only by colour |
| `link_underlined` | A paragraph holding a link in the same colour, underlined | No finding |

Every tab and paragraph is at least 48 dp tall, and the tabs at least
48 dp wide, and every colour is at least 4.5:1 on white, so the touch
target and contrast checks have nothing to find. Clicking a tab or link
does nothing. The tested rows follow a line not tested and a gap, as on
the other screens, as the checks read their colours.

Measured on the screen's first recording (2026-11-19), on the phone's
screenshot, with no OCR and no model:

- **Selected state:** each tab's `selected` flag is recorded, so the
  tree says which tab is selected and where it is. On the pixels, the
  selected tabs' text is 1.00:1 against the others' in lightness. Salads'
  strokes are 0.99 times as wide as the others', with no underline or
  bar; Lunch's are 1.38 times as wide, over the 1.3 for bold, and it is
  underlined.
- **Links:** from protocol 8 the companion app records each link's place
  in the text and its boxes on the screen (android-checks.md, step 29).
  The boxes fall on the links' words. Each link is 2.34:1 against the
  text around it; the plain link's strokes are 0.96 times as wide as the
  text's, and the underlined link was found underlined.
- **The paragraphs:** each is `clickable=false` and `focusable=true` in
  the tree; the link inside it is not an item of its own.

#### The link purpose screen

`link-purpose`, titled "Kitchen help", built on 2026-11-19 and opened
by the tour, for the link purpose checks (android-checks.md, step 30).
Every link is underlined, so the check of links shown only by colour
has nothing to find. The addresses are example.org's, and the tour
does not follow them. The expected findings are at AA. The screen stays
at the `unbuilt` stage until its recording is extracted and committed.

| Element | Built as | Expected |
| --- | --- | --- |
| `link_alone` | "Read more", a ClickableSpan that is all of its paragraph, after a paragraph saying what it is about | An error: a non-specific link with no context |
| `link_here` | "here" in "To see the full menu, tap here." | No context finding; a warning that it is not specific on its own |
| `link_described` | "knife safety guide" in a sentence | No finding |
| `link_details_soups`, `link_details_salads` | "Details" after "Soups:" and after "Salads:", links to two different addresses | On each, a warning that it is not specific on its own, and a warning for the same words to different addresses |
| `link_hours_first`, `link_hours_again` | "opening hours" in two sentences, links to the same address | No finding |

### Driven by a person

| Check | WCAG | Fails | Near miss |
| --- | --- | --- | --- |
| A label that is only placeholder text | 3.3.2 | A field with only a hint, which goes when typed in | A field with a visible label above it |
| An error shown only by colour | 1.4.1 | A field whose underline turns red on a bad value, with no text | A field that shows error text as well |
| Error messages that do not help | 3.3.1, 3.3.3 | "Invalid input" | "Enter a date as day, month and year, such as 4 March 2026" |

### Needing more recorded first

Screens for these can be built so they are ready, but each needs the
recorder or companion app to record more, so they come last.

- **Focus visible (2.4.7):** needs a Bluetooth keyboard.
- **Resize text (1.4.4):** needs the font scale recorded, and a pass at
  the largest scale.
- **Reflow (1.4.10):** needs the display's size changed for the test.
- **Identify input purpose (1.3.5):** needs autofill hints, which are
  not in the tree.
- **Pointer gestures, cancellation and dragging (2.5.1, 2.5.2, 2.5.7):**
  the touches show the gesture used, not whether a single-pointer
  alternative exists.
- **Language of software (3.1.1):** needs the locale recorded.

## Built checks, later coverage

After the checks not yet built, each built check gets its screen, so
every check has a fault and a near miss. Checks that need a person
(status messages, context changes, text gone by itself) are driven by
a person or timed by the screen itself, such as text that changes two
seconds after the screen opens, so the tour can still record them.

## Limits

- **Faults made on purpose.** A check that finds the faults its author
  made has shown only that; the real recordings stay the test of false
  alarms and of faults no one thought to make.
- **One phone.** The fixtures are recorded on the owner's Pixel; a
  fixture's sizes in pixels follow its density.
- **Flashing.** A screen that truly fails 2.3.1 could harm the person
  recording it. Whether to build one, and how it is opened (never by
  the tour, and only after a warning), is for the owner to decide
  before it is built.
- **Model answers.** A model's answer on a made-up screen may differ
  from its answer on a real one; the evaluation reports what it did,
  not how accurate it is in general.
