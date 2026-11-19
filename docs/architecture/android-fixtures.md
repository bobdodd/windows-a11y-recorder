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
  that keeps them in step with the app's source and the checks' names.

Not yet built: the extractor that turns a recording into compact
fixtures, the rule tests that run the checks on them, and the model
evaluation. They wait for the owner's first recording of the tour, so
that they are built against a real recording.

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

- **id:** the screen's name, as passed to `--es screen`.
- **title:** the activity's title, which the recorder uses to tell
  screens apart. It describes the screen's made-up content, such as
  "Basket", not the check, so the title checks have nothing to find.
- **framework:** `views` or `webview`.
- **how:** `tour`, opened by the tour script, or `person`, which also
  has `steps`, the steps a person takes on the screen, in at most five.
- **seconds:** how long the tour holds the screen.
- **stage:** `proof`, a built check proving the framework; `unbuilt`, a
  check not yet built; or `built`, a built check's later coverage.
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
is right. After that review, any unexpected finding fails the rule
tests, so a change to a check that adds false alarms is caught.

For a model check, the rule tests can only test the part decided
without a model: that the element is asked about, or left out, and
with what. The answer itself is scored by the model evaluation.

## Recording the tour

`scripts/Run-FixtureTour.ps1`:

1. finds the recorder's own adb, in
   `%LOCALAPPDATA%\Windows A11y Recorder\android\platform-tools`, so the
   tour talks to the recorder's adb server rather than starting one of
   another version, which would stop the server under a recording;
2. installs the fixture app if it is missing or older;
3. asks the person to press Record in the recorder, and waits for
   Enter;
4. opens each `tour` screen with `am start -W`, holds it for its
   `seconds`, and names it in the console as it goes;
5. says when it has finished, so the person can press Stop.

Screens marked `person` are not opened by the tour; the person opens
them from the app's index and follows their steps.

The tour is recorded with TalkBack on, as the owner's recordings of
real apps are, so that the fixtures are like the recordings the checks
meet. Whether TalkBack's focus outline changes any check's result on
the fixture screens is to be measured on the first recording.

## Rule tests

To be built after the first recording, from it.

- **The extractor** reads a recording of the tour and, for each visit
  to a fixture screen, writes a folder under
  `tests/fixtures/android/views/<screen>/`: the trees the checks read,
  one settled screenshot, OCR's lines with their bounds, and the events
  a check needs (such as TalkBack's speech). OCR is kept as text
  because Windows OCR does not run in the Linux build. The size of the
  folders is to be measured; only what the checks read is kept.
- **The harness** is a test in `tests/Recorder.Tests` that runs the
  checks on each folder and compares the findings with the expected
  answers: an expected finding missing, a finding on a near miss, or an
  unexpected finding fails the test, naming the screen, check and
  element.

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

### Driven by a person

| Check | WCAG | Fails | Near miss |
| --- | --- | --- | --- |
| A label that is only placeholder text | 3.3.2 | A field with only a hint, which goes when typed in | A field with a visible label above it |
| An error shown only by colour | 1.4.1 | A field whose underline turns red on a bad value, with no text | A field that shows error text as well |
| Error messages that do not help | 3.3.1, 3.3.3 | "Invalid input" | "Enter a date as day, month and year, such as 4 March 2026" |

### Needing more recorded first

Screens for these can be built so they are ready, but each needs the
recorder or companion app to record more, so they come last.

- **Link text (2.4.4):** needs links' spans, which the companion app
  does not record yet.
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
