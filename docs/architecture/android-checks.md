# Android Checks: Apps, Screens and Findings

## Status

Proposed 2026-11-11. Step 1 of "Build order", screens and visits, is
built (see "As built: step 1"), with rules added on 2026-11-12 for
screens drawn in one window, measured in the client's app. Step 2, the
display density and each app's version, is built (see "As built: step
2") and was checked on the phone on 2026-11-12. Step 3, the checks from
the tree and the findings list, is built (see "As built: step 3") and
was checked on the development PC on 2026-11-12 with recordings already
made (tests 11 to 13 of the validation plan). Step 4, contrast, is being
built: the phone's own screenshots (4a, checked on the phone on
2026-11-12), the colour measurement with the text found by OCR (4b,
checked on the development PC with tests 16 and 18's recordings), and
the text contrast check with its AA or AAA setting and its findings (4c,
see "As built: step 4c", tests 19 to 21 passed;
findings are since listed under each visit, tests 23 and 24 passed);
loading screens and screens with no fixed text follow decision 10,
tests 25 to 27 passed. Non-text contrast was measured first (see
"Step 5: measuring non-text contrast first"), and its check is built to
the owner's decisions (see "Step 5: the owner's decisions" and "As
built: step 5"), and was checked on the development PC on 2026-11-12
(tests 28 and 29 passed). Step 6, the screen's title and headings, is
built to the owner's decisions (see "Step 6: titles and headings") and
was checked on the development PC on 2026-11-12 (tests 30 and 31
passed). The general audit level, AA or AAA, is built (see "The audit
level"), and was checked on the development PC on 2026-11-12 (tests 32
and 33 passed). Further checks WCAG2ICT allows without AI, including 1.4.5 Images of
Text measured on the recordings to hand, are recorded as options in
`android-checks-candidates.md`. Step 8, images of text, is built (see
"Step 8: images of text"), and was checked on the development PC on
2026-11-12 (tests 34 and 35 passed). Step 9, Label in Name, is built
to the owner's decisions (see "Step 9: Label in Name"), and was
checked on the development PC on 2026-11-12 (tests 36 and 37 passed).
Step 10, clickable but not focusable, is built from the tree alone, to
the owner's decision (see "Step 10: clickable but not focusable"), and
was checked on the development PC on 2026-11-13 (tests 38 and 39
passed). Step 11, the checks of fields and states, is built from the
tree alone, to the owner's decisions (see "Step 11: fields and
states"), and was checked on the development PC on 2026-11-13 (tests 40
and 41 passed). Step 12, status messages (WCAG 4.1.3), is built to the
owner's decisions (see "Step 12: status messages"), and was checked on
the development PC on 2026-11-13 (tests 42 and 43 passed). Step 13,
timing (WCAG 2.2.1 and 2.2.2), is built to the owner's decisions (see
"Step 13: timing"), and was checked on the development PC on
2026-11-13 (tests 44 and 45 passed). Step 14, changes of context (WCAG
3.2.1 and 3.2.2), is built to the owner's decisions (see "Step 14:
changes of context"), and was checked on the development PC on
2026-11-13 (test 50 passed, on test 48's recording, after a fix). The owner's
decisions are in "Decisions (2026-11-11)", "Decisions on loading
screens (2026-11-12)" and "Decisions on the open questions
(2026-11-11)". Before any check is assigned a
result, the recording has to say which app and which screen each moment
belongs to, so that comes first in "Build order".

Statements below are marked as measured in a recording (with its
session), checked in this repository's source, checked in a cited
document, or inference.

## The problem

The owner needs the recorder to test a client's Android app (owner,
2026-11-11). The analysis built so far covers only browser page visits:
`Recorder.Analysis` has no Android code, and a recording with no page
visits asks no model and says so (checked in source). The phone's
accessibility tree, events, screen video, touches and TalkBack's text
are recorded ([Android recording](android-recording.md)), but nothing
yet turns them into findings.

A finding needs a place to belong to. "The Navigate up button is 40 by
40 dp" means little without the app and the screen it was on, and the
same button seen in 30 tree snapshots of one visit is one finding, not
30. So this plan first defines how the recorder identifies apps,
screens, visits and items on its own, then the checks that run against
them.

## Decisions (2026-11-11)

1. **AI-free where possible.** "As far as possible testing should be
   AI-free both for cost and repeatability, I expect to use AI to help
   with summary reports and vision-centric tests only" (owner). Every
   check in this plan is a rule applied by the recorder's own code; no
   model is asked. The owner clarified the rule on 2026-11-15: "The rule
   is really AI only when non-AI cannot do the test. The biggest group
   is vision AI." (`android-vision-checks.md`)
2. **Findings in the player only.** The findings are shown in a list in
   the player, which moves the playhead to each one. A findings report
   is later work.
3. **Touch targets at 48dp and 24dp.** A target under 48dp is flagged,
   and one under 24dp is flagged separately, as two levels.
4. **Contrast from the screen video is included,** marked as
   approximate because the video is compressed.
5. **Screens are identified automatically.** Analysis and testing are
   to be fully automatic throughout; editing may be added later, but
   for now everything is automatic (owner). The recorder's rules decide which screen each moment
   belongs to; the player shows the rule that decided, but there is no
   renaming, merging or splitting.
6. **Dialogs, menus and bottom sheets are states of a screen.** A
   finding in a dialog belongs to the screen beneath, labelled as in
   that dialog. This follows the policy of the project's unique view
   proposal, "Unique View Identification in Accessibility Testing
   Recordings" (a project note, not in this repository), that "a modal
   dialog is normally a child state of the underlying view".
7. **Within a recording first.** Screens are matched within one
   recording. Matching the same screen across recordings waits for the
   test data model. Replaced on 2026-11-14 for reports: a project's
   report matches a screen across its recordings by the same screen
   key, as the owner chose; the checks themselves stay within each
   recording. See [projects](projects.md).
8. **Findings belong to visits (2026-11-12).** A finding is listed
   under the visit it was seen in, as the screen's content can change
   between visits: "I expected the checks to relate to the visit in
   which they occur (since content can change)" (owner), choosing
   "Under each visit" over one finding per screen naming its visits.
   This replaces the merging across visits first planned in "Items and
   findings".

## Decisions on loading screens (2026-11-12)

Found in test 24 (see the validation plan): the client's app shows a
loading screen, a sun and a moon over "Loading your personal sleep
learning space.", before the Diary and Profile tabs, and every text on
its Profile screen is inside a scrolling container, so the Profile
screen had no first fixed text and was counted as the loading screen.

10. **A loading screen is its own screen, and a screen with no fixed
    text is named by its first text.** The owner chose "Its own screen"
    for the loading screen, and "Its first text" for a screen whose
    every text is in a scrolling list: when the first fixed text goes
    away and the window still has text, a new visit starts, named by its
    first text ("Bob Dodd" for Profile). Scrolling never starts a visit.
    As the video lags the tree (the tree shows "Sleep Diary" at
    00:00:31.326, the frame then is still the loading screen's, and the
    first frame with "Sleep Diary" is from 00:00:31.340, measured in
    test 16's recording), a visit, and a finding from the tree, is shown
    at the picture of its settled snapshot, not at the snapshot's time.

## What a recording holds

Measured in the phone recording of test 64 of the
[Android recording plan](../validation/android-recording-plan.md),
`20261111-002648-06be83ce21ec47309bd4bc68b4ae1cb4` (Settings on the
Pixel 10 Pro XL, Android 17, opening "Safety and emergency" with
TalkBack on), exported with `tests/RecordingEventExport`:

- **Windows.** Each tree snapshot lists every window. The app's window
  was `TYPE_APPLICATION`, active and focused, titled "Settings"; the
  status bar, navigation bar and a small system window were
  `TYPE_SYSTEM`. Opening "Safety and emergency" gave a new
  `TYPE_APPLICATION` window with a new ID (1343, after 1316), titled
  "Safety and emergency".
- **The activity.** That opening raised a `TYPE_WINDOW_STATE_CHANGED`
  event whose class name was `com.android.settings.SubSettings` and
  whose text was "Safety and emergency". Android documents the event's
  class name only as "the class name of the source", and its text as
  "the text of the source's sub-tree, including the pane titles"
  ([AccessibilityEvent](https://developer.android.com/reference/android/view/accessibility/AccessibilityEvent#TYPE_WINDOW_STATE_CHANGED));
  that it names the activity is measured here for an activity window,
  not documented. `SubSettings` is one activity that Settings uses for
  many of its pages, so the activity alone does not name a screen
  (inference from its name and this recording; one page was opened).
- **The window title.** Android takes it "from the `Window`'s title, or
  its `WindowManager.LayoutParams` if that is unset"
  ([AccessibilityWindowInfo.getTitle](https://developer.android.com/reference/android/view/accessibility/AccessibilityWindowInfo#getTitle())).
  Here it matched the page's heading.
- **Headings and view IDs.** The new page's toolbar was an item marked
  heading, with content description "Safety and emergency". Of its 89
  items, 49 had view IDs such as `com.android.settings:id/content_parent`.
  Apps written with Jetpack Compose expose view IDs only where the app
  enables `testTagsAsResourceId`
  ([Compose interoperability](https://developer.android.com/develop/ui/compose/testing/interoperability)),
  so a client's app may have few or none.
- **Windows move while they open.** The new window's bounds were
  191 to 1271 pixels across at 16.57 s, 18 to 1098 at 16.69 s, and 0 to
  1080 from 17.00 s: it slid in over about half a second. Each snapshot
  also carries the window's transition time, which changed with each
  position. A check run on a snapshot taken during the slide would
  report items in the wrong place.
- **The screen video** is 1080 by 2404 pixels, the display's own size,
  H.264 at 8 Mbit/s (checked in source: `ScrcpyProtocol.VideoBitRate`,
  no size limit is passed), so tree bounds and video pixels share one
  scale when the phone is not turned. PNG frames are made of each
  picture that differs from the one before (checked in source:
  `PhoneFrames`).
- **Pane titles.** None of the app's items had one in this recording;
  the status bar's did ("Status bar"). Android sends
  `TYPE_WINDOW_STATE_CHANGED` with `CONTENT_CHANGE_TYPE_PANE_APPEARED`
  when an item with a pane title appears
  ([AccessibilityEvent](https://developer.android.com/reference/android/view/accessibility/AccessibilityEvent#CONTENT_CHANGE_TYPE_PANE_APPEARED)),
  which is how a screen that replaces its content inside one window can
  be seen.

Not recorded before step 2, and recorded since (see "As built: step 2"):

- **Density.** Bounds are in pixels; dp needs the display density.
  Before step 2 only a forced "Display size" setting was recorded
  (checked in source: `AndroidSettingsRecords`, `display_density_forced`),
  not the density in use.
- **The app's version.** Before step 2 only the companion app's version
  was recorded (checked in source: `RecorderLink`). A client report
  needs the version of the app under test.

Not recorded, and needed:

- **Text size and link spans.** The tree has no text size, and an
  item's text is recorded as plain text, without the links inside it
  (checked in source: `AccessibilityTrees.java`).

## Apps

The app on screen at a moment is the package of the active
`TYPE_APPLICATION` window, from its items' package name. Windows of
other types (the status bar, navigation bar, keyboard and other system
windows) are not an app's screen; their items are not checked, as they
are the phone's, not the app's.

Every app seen is listed in the player, each with its screens, and
checked. When the app on screen changes (another app opened, the home
screen, a share sheet from another package) a new visit starts, to that
app's screen.

The app's version is recorded at the end of the recording, for each
package with a window in the tree snapshots, with `adb shell dumpsys
package <package>`, which prints `versionName` and `versionCode`
(checked in source: `Settings.dumpPackageLPr` in AOSP; the command's
output is not documented as an interface, so the recorder keeps the
lines it read). See "As built: step 2".

## Screens and visits

The words follow the unique view proposal (see decision 6), as
[the test data model](test-data-model-2026-10-19.md) does: a **visit** is one continuous
period a screen is shown; a **screen** (a unique view) is the identity
visits belong to. A return to an earlier screen is a new visit to the
same screen.

### When a visit starts

A new visit starts when, in the app on screen:

1. **The active application window is a new one.** A new window ID for
   the active, full-size `TYPE_APPLICATION` window (measured above: a
   new activity gave a new window).
2. **The window's state changes.** A `TYPE_WINDOW_STATE_CHANGED` event
   from the app's window whose class name or text differs from the
   visit's, or the window's title changing. This covers apps that change
   screens inside one window. An event for a pane (its content change
   types include `CONTENT_CHANGE_TYPE_PANE_TITLE`, `_PANE_APPEARED` or
   `_PANE_DISAPPEARED`) does not start a visit: a pane is a state of the
   screen (decision 6, and "States" below).
3. **Another app comes to the front,** as above.
4. **The window's first fixed text changes,** in the same window with
   the same title (added 2026-11-12, measured in the client's app
   below). The **first fixed text** is, in the tree's order, the first
   visible text or content description of an item that is not
   clickable, has no clickable or scrollable item around it, and is in
   no titled pane. A snapshot with no fixed text, as while a screen is
   drawn, changes nothing by this rule.
5. **The first fixed text goes away and stays away** for half a second
   or more while the window still has text (decision 10). The visit
   starts when it went away. A fixed text gone for less than half a
   second, as while a screen is redrawn, changes nothing. A fixed text
   seen after such a visit starts another visit, by rule 4.

Scrolling, focus moving, text typed, items changing, and content changes
that keep the window, its class name, its title and its first fixed text
do not start a visit. Scrolling a screen with no fixed text changes its
first text, but starts no visit.

### What names a screen

Within a recording, two visits are to the same screen when they share a
**screen key**:

- the app's package,
- the class name from the window-state event that started the visit
  (the activity, for an activity window),
- the **screen title**: the window title when the app's window has one;
  otherwise the text of the window-state event that started the visit;
  otherwise the text of the first heading, in the tree's order, in the
  app's window. A pane's title names a state, not the screen, as a pane
  is a state.

The player names each screen by its title and app, such as "Safety and
emergency (Settings)", and shows which of the three gave the title.

When visits with the same title in the same app have different first
fixed texts, the title names no one screen: those visits are named, and
matched, by their first fixed text, and the player says how many
screens share the title. A screen with none of the three titles but a
first fixed text is named by that text. (Added 2026-11-12, for the
client's app below.)

A screen with no first fixed text is named, and matched, by its **first
text**: in the tree's order, the first visible text or content
description of an item that is not clickable, has no clickable item
around it and is in no titled pane, inside a scrolling container or not.
The player says "named by its first text, as it has no fixed text and 5
screens share the title 'Nova SleepCare'", or "named by its first text,
as it has no title and no fixed text". The first text is that of the
visit's first snapshot (decision 10).

When none of the three gives a title, the screen is named by its
activity and a number in order of first visit, such as "SubSettings,
untitled screen 2", and the key adds the sorted set of view IDs in the
app's window, so different untitled screens are kept apart. Where there
are no view IDs either, visits with the same activity and no title are
treated as one screen; the player says so. This is a known limit for
apps that expose neither titles nor IDs.

### States: dialogs, menus and sheets

Decision 6 makes these states of the screen beneath. A visit's state
changes, without a new visit, when:

- a `TYPE_APPLICATION` window that does not cover the app's full window
  appears above it (inference: dialogs and popup menus are drawn as
  their own smaller windows; to be measured on the client's app), or
- a pane appears inside the app's window over its content, such as a
  bottom sheet with a pane title.

The state is named from its window title, pane title or first heading,
as a screen is, such as "Safety and emergency (Settings), in dialog
'Remove account?'". A finding in that state belongs to the screen, with
the state named. A dialog from another package over the app, such as
Android's permission dialog, is a state of the app's screen, and its
findings are labelled with that package so they are not counted as the
app's.

The keyboard (`TYPE_INPUT_METHOD`) is not a state and is not checked.

### Settled snapshots

The checks run only on **settled** snapshots: those in which the app's
window has the same bounds and transition time as in every later
snapshot of that window in the visit, and after which the window is not
listed again within half a second (added 2026-11-12: in the client's
app a new screen is drawn in the same window with the same transition
time, its snapshots 10 to 20 ms apart). A snapshot taken while a window
slides in, as at 16.57 s above, or while a screen is drawn, is shown in
the tree view as now, but no check uses it.

## Items and findings

An item is identified within a screen by, in order:

1. its view ID, with its position among items of that ID when several
   share one (rows of a list);
2. otherwise its class, its text or content description, and the view
   IDs and classes of the path from the window's root.

A **finding** is one check's result for one item in one visit to a
screen, at one level. It lists every settled snapshot of the visit in
which it was seen, with its time, so the player can move to the first
and step through the rest. The same item failing the same check on two
visits to the same screen is two findings, one under each visit
(decision 8); as first planned it was one finding with two visits.

Each finding keeps the rule that gave it, the values measured (such as
"40 by 40 dp" or "contrast 3.2 to 1"), the snapshot or frame it was
measured in, and whether the value is exact (from the tree) or
approximate (from the video).

## The checks

The rules follow Google's Accessibility Test Framework for Android
(ATF), version at commit `c65cab0`
([repository](https://github.com/google/Accessibility-Test-Framework-for-Android/tree/c65cab02b2a845c29c3da100d6adefd345a144e3),
Apache License 2.0), which runs on the same accessibility items. Each is
rewritten in C# against the recorded snapshot, not linked, as ATF needs
a running Android device; the code says which ATF check each follows,
and its licence notice is kept with it. ATF's results are "error",
"warning", "info" and "not run" (checked in source:
`AccessibilityCheckResultType`); the recorder keeps those words. Of the
checks below only Duplicate speakable text gives "info", for items none
of which is clickable.

### From the tree, exact

| Check | Rule | Data |
|---|---|---|
| Touch target size | A clickable or long-clickable item visible to the user under 48 by 48 dp, or 32dp against the screen's edge or in the keyboard's window, as ATF's defaults ([TouchTargetSizeCheck][atf-touch]). Decision 3 adds a second level under 24 by 24 dp, the size in WCAG 2.5.8 Target Size (Minimum), with dp standing for the CSS pixel as WCAG2ICT advises for non-web software ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)). The 24dp level uses WCAG's spacing exception, a 24dp circle centred on each undersized target not meeting another target or circle, which the bounds answer, instead of ATF's edge allowance; ATF would scale its edge size down with the target size. | Bounds, clickable, visible; density (to add) |
| Speakable text present | A visible item important for accessibility that a screen reader would focus has no text, content description, label from another item or text from its children ([SpeakableTextPresentCheck][atf-speakable]). | Text, content description, labelled by, children |
| Duplicate speakable text | Two or more items in the active window speak the same text; a warning when one is clickable ([DuplicateSpeakableTextCheck][atf-dup-text]). | As above, clickable |
| Duplicate clickable bounds | Two or more clickable items in the active window have the same bounds ([DuplicateClickableBoundsCheck][atf-dup-bounds]). | Bounds, clickable, long-clickable |
| Editable content description | An editable text field has a content description, which replaces what it says ([EditableContentDescCheck][atf-editable]). | Editable, class, content description |
| Redundant description | A content description that includes the item's type, state or action, such as "button" or "checked", in English ([RedundantDescriptionCheck][atf-redundant]). | Content description, class |
| Class name | An item whose accessibility class name is empty, or outside Android's own UI packages, so a screen reader cannot say what it is ([ClassNameCheck][atf-class]). | Class name |
| Traversal order | Items whose "traversal before" and "traversal after" links form a loop, or conflict ([TraversalOrderCheck][atf-traversal]). | Traversal before, after |

### From the video, approximate (decision 4)

| Check | Rule | Data |
|---|---|---|
| Text contrast | The colours within a text item's bounds in the frame shown at the snapshot's time, separated into background and text as ATF's colour swatch does; under 3 to 1 is an error, and from 3 to 4.5 to 1 a warning, as ATF does when text size is unknown ([TextContrastCheck][atf-text-contrast]). The ratios are WCAG 1.4.3's ([WCAG 2.2](https://www.w3.org/TR/WCAG22/#contrast-minimum)). | Bounds, text; the frame |
| Image contrast | The same for an image item, against 3 to 1 ([ImageContrastCheck][atf-image-contrast]). | Bounds, class; the frame |

Both are approximate because the video is compressed with H.264, which
stores colour at lower resolution than brightness (inference from the
format; how much it changes a measured ratio is to be measured against
screenshots). Each finding says "approximate, from the video". A frame
is used only when no picture change falls between the snapshot's time
and the frame's.

### Not in the first version

- **Text size** and **link purpose** need data the tree lacks (see
  "What a recording holds"). Recording an item's link spans is a change
  to the companion app, for later.
- **Clickable span** applies only before Android 8, API 26
  ([ClickableSpanCheck][atf-clickable-span], `APPLICABLE_UNTIL_ANDROID_SDK_VERSION = 26`).
- **Unexposed text** needs OCR of the frame and is vision-centric; it
  can follow with the vision work.
- **Timers** wait for the decompiled APK, as the owner said.

## The player

A new tab, "Checks", lists the findings of the open recording, grouped
by app, then screen, then visit, then check (decision 8). Each row says the check, the level,
the item, the measured value and the state, such as "Touch target under
48dp: 'Navigate up' button, 40 by 40 dp, Safety and emergency
(Settings)". Choosing a row moves the playhead to the finding's first
snapshot, draws its bounds on the phone's picture, and selects the item
in the "Accessibility tree" tab; "Next time" and "Previous time" step
through the snapshots it was seen in. Each screen's row says how its
title was found. Text wraps in the tab; there is no horizontal
scrollbar.

### The Summary tab

The owner, 2026-11-18, with a screenshot of the Checks window: "The
summary for a recording take most of the window even on full-screen and
is unreadable text. The summary needs to be its own tab, and formatted
for humans to read". Until then the counts of every check, the audit
level and the project's models were one paragraph above the list. Built
2026-11-18 (`PhoneChecksSummary` in `Recorder.Session`,
`MainWindow.ChecksSummary.cs`).

- The Checks window has two tabs, Findings and Summary. Above both are
  the settings the checks run with, the audit level and "Phone turned
  for", and "Ask the project's model", as they change what both tabs
  show. Where the model cannot be asked, why is said under them, beside
  the button it disables.
- Findings holds Show current visit, Previous time and Next time, one
  short line, and the list. The line says only what changes with the
  playhead or while the checks run: the apps, screens and visits, the
  visit at the playhead, whether a check is still running or could not
  run, and which time of the chosen finding is shown.
- Summary holds the rest, in sections, each with a heading (a heading to
  UI Automation, level 2), its result in bold on a line of its own, such
  as "11 findings: 5 errors, 6 warnings.", each count on a line of its
  own after a drawn bullet, and how the check judges, or what it does not
  look at, after them. The bullet is a shape, so a screen reader does not
  say "bullet" before each line and it is not copied with the text.
- The sections, in order: Recording; Findings by check, every check with
  findings and how many at each level, or that the checks are still
  running; the audit level, with each WCAG criterion it holds to on a
  line; the project's models for checks, one use to a line; then each
  check or group of checks, the checks of the tree first, then the text
  read on the screens' pictures (the OCR engine, the snapshots measured
  and any that could not be), then the contrast checks and the checks
  that use OCR. Text field shown only by a bottom line has its own
  section, apart from non-text contrast.
- What the model said of a check's questions is split at each "; "
  outside brackets, each part a line of its own after "Model's answers:".
- The tab's text wraps, with no horizontal scrollbar, and is no wider
  than 1,100 pixels, so its lines stay readable in a maximised window.
  The text is selectable and copied with Ctrl+C, as elsewhere; the tab's
  scroll area is a tab stop, so the arrow keys and Page Up and Page Down
  scroll it.
- The sections are laid out again only when what they say changes, not
  as the playhead moves, so a selection in them holds.
- The counts are those the paragraph gave. Where a sentence held several,
  it was split or reworded so that each stands as a line.

Pushed at 1e7a5fb. On 2026-11-18 the owner checked the Summary tab on the
target machine: "I have checked the summary tab and it appears correct."

### Toasts for the model checks

The owner, 2026-11-18: "The next issue I have is with the checks window
and the "Ask to project's model" button. We need toasts to say when the
checking has started and when it is complete." Until then the button
queued the model checks with nothing in the Checks window to say so: the
queued line and the line when the task ended were in the Analysis
window's status line, and screen readers were told them. Built
2026-11-18 (`ModelChecksProgress` in `Recorder.Analysis`,
`MainWindow.ChecksToast.cs`).

- The Checks window shows one toast at a time, at its foot, under the
  tabs. A toast says what the task of the model checks did since the last
  read of the queue, every 3 seconds: queued and not yet started, as the
  worker may be busy with another task; started; complete; failed, with
  the reason; or cancelled. A new toast replaces the one shown.
- "Model checks complete." is shown once the model's answers are in the
  Findings and Summary tabs, not when the worker ends, and says how many
  of the questions the rules leave are still not judged, if any, as the
  Summary tab gives them by check.
- Where the button could not queue the task, a toast says why.
- The toast stays until its Close button is pressed, the next toast
  replaces it or another recording is opened. A message that goes by
  itself can be missed or be gone before it is read (WCAG 2.2.1).
- It is a row of its own under the tabs, not drawn over them, so it never
  hides the item with keyboard focus (WCAG 2.4.11), and it takes no focus
  as it appears. Screen readers are told its text as it is shown, as a UI
  Automation notification, activity `Recorder.App.Analysis`; the model
  checks' lines in the status line are not spoken as well, so a screen
  reader does not say both. Closing it with the keyboard moves focus to
  "Ask the project's model" where it is enabled, else to the tab shown.
- Its text is selectable and copied with Ctrl+C, as elsewhere, and wraps.
  Its edge, #75736F, is 3.07 to 1 against its own background, #2A2926,
  and 3.6 to 1 against the window's, #1C1B19 (WCAG 1.4.11); its text,
  #CDCCCA, is 9.1 to 1 on its background. Close is 44 by 44 pixels or
  more.
- The toasts are for the task of the model checks, whether "Ask the
  project's model" or the Analysis window's Run again queued it. The
  Android report and summary, which ask the same models, have no toasts.
- While the Checks window is closed, the toast is kept and is there when
  it opens; screen readers are told it either way. The player's own
  window shows no toast.

Pushed at 0f91810. On 2026-11-18 the owner ran it on the target machine:
"I see the toasts." Which toasts were seen, and what screen readers
said, were not reported.

The checks run when the recording is opened, from its stored snapshots
and frames. Keeping their results beside the recording was proposed in
case they were slow; step 3 measured them at 0.1 to 0.2 seconds for the
recordings to hand (see "As built: step 3"), so they are run each time
the recording is opened and nothing is kept.

## Build order

1. **Screens and visits.** `Recorder.Session` divides a phone recording
   into apps, visits, screens and states, from its snapshots and events,
   with tests from this recording's export and made-up cases (a new
   window, a window-state change in one window, a dialog window, a pane,
   another app, a return). The player shows the visits in the timeline
   and the "Checks" tab lists screens, without findings yet. Checked on
   the development PC with a recording of the client's app.
2. **Density and app version.** Record the display density and each
   app's version.
3. **The exact checks** from the tree, with settled snapshots and the
   findings list.
4. **The approximate contrast checks** from the video, measured first
   against screenshots taken on the phone at the same moments.
5. **Non-text contrast** (WCAG 2.2's 1.4.11), measured first on the
   recordings to hand, with its rules decided by the owner from the
   measurement (see "Step 5: measuring non-text contrast first"). Built
   (see "As built: step 5").
6. **The screen's title and headings**, one check for each discrete
   issue (see "Step 6: titles and headings"). Built.
7. **The audit level**, AA or AAA, guiding the checks and their
   reporting (see "The audit level"). Built, and checked on the PC.
8. **Images of text** (WCAG 1.4.5, and 1.4.9 at AAA), from the words OCR
   already reads (see "Step 8: images of text"). Built, and checked on
   the PC. Four rules without a model and a question for the project's
   model for vision-based checks about what they leave were added on
   2026-11-16 (`android-vision-checks.md`, "Images of text, as built").
9. **Label in Name** (WCAG 2.5.3), a control's content description
   against the text inside it (see "Step 9: Label in Name"). Built, and
   checked on the PC.
10. **Clickable but not focusable** (WCAG 2.1.1), from the tree alone
   (see "Step 10: clickable but not focusable"). Built, and checked on
   the PC.
11. **Fields and states** (WCAG 3.3.2, 4.1.2 and 3.3.1), from the tree
   alone (see "Step 11: fields and states"). Built, and checked on the
   PC.
12. **Status messages** (WCAG 4.1.3), from the tree, touches and events
   (see "Step 12: status messages"). Built, and checked on the PC.
13. **Timing** (WCAG 2.2.1 and 2.2.2), from the tree, touches, the
   phone's settings and the phone's video (see "Step 13: timing").
   Built, and checked on the PC.
14. **Changes of context** (WCAG 3.2.1 and 3.2.2), from the tree,
   touches, keys and events (see "Step 14: changes of context"). Built,
   and checked on the PC.

## As built: step 1

Built 2026-11-11 in `src/Recorder.Session/PhoneScreens.cs`
(`PhoneScreenTimeline`), with tests in
`tests/Recorder.Tests/PhoneScreenTimelineTests.cs`. The rules above are
applied as follows; where this section is more exact than the sections
above, it is what the code does.

- **What it reads.** The playback index (version 26 when step 1 was
  built; 27 since step 2) keeps every
  `TYPE_WINDOW_STATE_CHANGED` event whole, and each compact tree
  snapshot keeps, for every window whose items were taken, what the
  items say about the screen (`PhoneScreenEvidence`): the root item's
  package, the first visible heading's text (or the first text inside
  it, as the Settings toolbar's heading had none of its own), the pane
  titles, the first fixed text of the window and of each titled pane,
  and the number and a hash of the sorted distinct view IDs.
  A window whose items were not taken in a snapshot keeps the evidence
  from the last snapshot that took them. An index from an older version
  is derived again, so earlier recordings get their screens.
- **The app's window** in a snapshot is, of its `TYPE_APPLICATION`
  windows that are not dialogs, bottom sheets or popups by the class
  name of their latest window-state event, the topmost of those covering
  at least half the screen; else the current visit's window while it is
  listed; else the largest. Half, not all, so a window sliding in counts
  (at 16.57 s in test 64 it covered 82% of the screen). A snapshot with
  no application window, as between two activities (16.40 to 16.41 s in
  test 64), changes nothing.
- **When a visit starts.** A visit to a new window starts at that
  window's window-state event when the event came after the current
  visit began, as the event comes before the first snapshot listing the
  window (16.51 s against 16.57 s in test 64); otherwise at the
  snapshot. A new window from the same app is "a new window"; from
  another package, "another app". A change of first fixed text starts a
  visit at the snapshot that shows it. A first fixed text gone for
  500 ms or more, with text left in the window, starts a visit at the
  snapshot where it went (the cause "the first fixed text going away"),
  and the snapshots from then on move to it; it is decided at the next
  snapshot 500 ms or more later, at a window-state event, or at the end
  of the recording (decision 10, index version 29). A visit whose window, app,
  activity, title, window-state text and first fixed text are all
  unchanged goes on.
- **Screens.** Visits are matched to screens once the recording is
  read, in order: same package, same title, and the same activity or an
  activity unknown on either side, as for the window a recording begins
  in, which has had no window-state event. An untitled screen is also
  matched by its view-ID hash. The heading and the view-ID hash of a
  visit are taken from its settled snapshots first. A title shared by
  visits with different first fixed texts gives way to the fixed text,
  as in "What names a screen".
- **States.** Application windows above the app's window, and windows
  that are dialogs, bottom sheets or popups by their class name, are
  states, named by their window title, window-state text or first
  heading, and labelled with their package when it is not the app's.
  Each pane title in the app's window is a state; the player also shows
  the state's first fixed text, as a pane titled "Dialog" says little.
  A state runs from the
  first snapshot it is in to the first it is not in, or to the end of the
  visit.
- **Apps** are named by package; the app's own name is not recorded.
- **The player** has a "Checks" tab listing each app, its screens with
  how each was named and its activity, each screen's visits with their
  times, what started them, whether they are a return and how many of
  their snapshots are settled, and each visit's states. A row moves the
  playhead to the start of its visit or state, or of the first visit for
  a screen or an app. (Since decision 10, a visit's row, and a screen's
  or an app's, moves it to the picture of the visit's first settled
  snapshot, or, with none settled, to the first screenshot in the visit
  or else the last frame before it ended; a state's row still moves it to
  the state's start.) The visit at the playhead is marked; "Show current
  visit" scrolls to it. The timeline has a fifth strip, under the
  TalkBack said strip, with each visit in brown and its screen's name
  where it fits (white on `#99601A` is 5.2 to 1; the brown against the
  timeline's `#201F1D` 3.2 to 1); clicking it moves the playhead there.

Measured against test 64's recording with a throwaway program on the
development machine: one app, two screens, "Settings" (named by its
window title, activity unknown) from 1.209 s and "Safety and emergency"
(named by its window title, activity `SubSettings`) from 16.511 s, with
two of its five snapshots settled (17.00 and 17.02 s). With the half
second rule added on 2026-11-12, one of those five is settled (17.02 s)
and four of the first visit's seven.

### Measured in the client's app (2026-11-12)

The owner recorded the client's app, Nova SleepCare
(`com.pss.novasleepcare`), in session
`20261112-005243-5528d67531dd4441a50b2c2985350c59`: Home, then Learn,
then the Understanding Melatonin module, then Diary, then the Good
morning card, which opened a dialog; then Back twice. The first build of
step 1 found 2 apps, 2 screens and 3 visits: all four of the app's
screens were one screen, "Nova SleepCare". Measured in the recording:

- every screen was in one window (ID 264), titled "Nova SleepCare" from
  start to end, with one transition time (51734729) throughout;
- moving between screens raised only `TYPE_WINDOW_CONTENT_CHANGED`
  events from `android.view.View` items: no window-state event and no
  title change (inference: the app is drawn with Jetpack Compose,
  corrected in step 10: its tree is as Flutter makes one);
- no item was a heading, and the only view IDs were Android's own
  (`android:id/content`, `android:id/navigationBarBackground`), the same
  on every screen;
- the root pane's title was a single space; the dialog was a pane
  titled "Dialog" in the same window, raised with a pane-title
  window-state event, and while it was open the tree held only the
  dialog's items;
- each screen's first fixed text was its own and stayed while it was
  shown: "Welcome, Bob Dodd" (Home), "Sleep Learning Library" (Learn),
  "Sleep Learning" (the module, with "Module: Understanding Melatonin"
  under it), "Sleep Diary" (Diary), and in the dialog's pane "Last
  Night's Sleep";
- a new screen was drawn over about half a second with a snapshot every
  10 to 20 ms (8.95 to 9.39 s for Learn), one at 13.91 s with no text;
- Back on Sleep Diary left the app for the launcher: the recording has
  two Back presses (41.06 s, which closed the dialog, and 42.04 s);
- none of the bottom bar's tabs (HOME, LEARN, DIARY, PROFILE) was
  marked as selected.

With the rules added on 2026-11-12, the throwaway program gives 2 apps,
5 screens and 6 visits: "Welcome, Bob Dodd", "Sleep Learning Library",
"Sleep Learning" and "Sleep Diary" (each named by its first fixed text,
as 4 screens share the title "Nova SleepCare"), the launcher's "Home"
(named by its window title), and a return to "Sleep Diary" at 44.135 s,
after the launcher. The dialog is a state of the first Sleep Diary
visit, pane "Dialog" with first fixed text "Last Night's Sleep", from
23.157 to 41.307 s. Settled snapshots: Home 1 of 1, Learn 1 of 49, the
module 2 of 42, Sleep Diary 4 of 52 and 1 of 3, the launcher 2 of 12.

Not yet known:

- apps whose first fixed text changes while one screen is shown, such
  as a clock or a counter at the top; each change would start a visit
  to a new screen;
- two screens of one template with the same first fixed text, as two
  modules might both begin "Sleep Learning"; they would be one screen
  (decision 8 keeps content titles apart, but this text is not the
  content's title);
- apps where each screen is a titled pane, which are states here;
- whether dialogs, menus and bottom sheets in other apps are their own
  windows, and what class names their window-state events carry;
- whether turning the phone gives the app's window a new ID, which
  would start a new visit to the same screen;
- moving between two screens that both have no fixed text, as from one
  scrolling list to another with no fixed heading; no visit starts, as
  scrolling changes the first text too (decision 10).

With decision 10, test 16's recording
(`20261112-154851-32c7b61a5552453bbf5febffcca978c7`) gives, with the
throwaway program, 1 app, 5 screens and 7 visits: "Welcome, Bob Dodd"
(Visits 1 and 7), "Sleep Learning Library" (Visit 2), the loading
screen "\u2600\uFE0F\U0001F319" (Visits 3 and 5, from 00:00:31.048 to
00:00:31.326 and from 00:00:42.155 to 00:00:42.502, neither with a
settled snapshot), "Sleep Diary" (Visit 4) and "Bob Dodd" (Visit 6, from
00:00:42.502 to 00:00:54.359, started by the first fixed text going
away, named by its first text). The visits are shown at 00:00:02.655,
00:00:28.202, 00:00:31.322, 00:00:42.154, 00:00:42.489, 00:00:47.633 and
00:01:06.223, and a frame from each, looked at, shows its screen: Home,
Learn, the loading screen, Sleep Diary, the loading screen, Profile and
Home. Four other recordings, among them the one above
(`20261112-005243`) and test 18's, give the same visits as before. The
findings are the same: at AA 11, from 110 lines, 99 passed; at AAA 12.
The touch target finding for "Help & FAQ Get Support" is now shown at
00:00:47.633, the picture of its snapshot, not at 00:00:42.594, the
snapshot's time.

## As built: step 2

Framework metadata extends the per-app records without adding a finding:
see [Android app framework metadata](android-framework-metadata.md)
for the supported signatures, provenance, unknown results and limits.

Built 2026-11-12 in `src/Recorder.Collectors.Android/AndroidDeviceCollector.cs`
(records in `src/Recorder.Contracts/AndroidDeviceRecords.cs`, channel
`device.android`) and `src/Recorder.Session/PhoneDeviceTimeline.cs`, with
tests in `tests/Recorder.Tests/AndroidDeviceTests.cs`. What `wm density`
and `dumpsys package` print was first read in AOSP's source (main
branch, read 2026-11-12), then measured on the Pixel 10 Pro XL in tests
7 to 9 of the validation plan, as listed at the end of this section.

- **The density.** `adb -s <serial> shell wm density` is run when the
  recording starts (in the background, so the start does not wait) and
  when it stops. It prints "Physical density: N", and "Override density:
  N" when the density in use differs (checked in source:
  `WindowManagerShellCommand.printInitialDisplayDensity`,
  [WindowManagerShellCommand.java](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/services/core/java/com/android/server/wm/WindowManagerShellCommand.java)).
  The density in use is the override when there is one, else the
  physical density; 1 dp is that density over 160 pixels (checked in a
  cited document:
  [DisplayMetrics.DENSITY_DEFAULT](https://developer.android.com/reference/android/util/DisplayMetrics#DENSITY_DEFAULT)).
  The lines printed are kept in the record.
- **Changes during the recording.** The settings collector already
  records each change of `display_density_forced` ("Display size"). A
  number starts a period with that density; an empty value, no value or
  the physical density's own number returns to the physical density, as
  Android writes an empty value when the density is reset (checked in source:
  `DisplayWindowSettings.setForcedDensity`,
  [DisplayWindowSettings.java](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/services/core/java/com/android/server/wm/DisplayWindowSettings.java)).
  The properties panel now shows an empty value as "the phone's own size
  (reset)". A density read at the stop that differs from the last period
  starts one more period at the stop, marked as found only by the read at
  the stop.
- **The versions.** The accessibility tree collector notes the package
  of each window whose items it took, from the window's first item with
  a package name, in the order first seen. When the recording stops,
  after the tree collector has stopped, `adb -s <serial> shell dumpsys
  package <package>` is run for each one, with a 15 second limit each. A
  package name is checked against Android's letters, digits, underscores
  and dots before it is put in the command. The version is read from the
  first "Package [name]" block under the "Packages:" heading, up to the
  next package or heading, so an older copy under "Hidden system
  packages:" (an updated system app's factory version) is not taken
  (checked in source: `Settings.dumpPackagesLPr` and `dumpPackageLPr`,
  [Settings.java](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/services/core/java/com/android/server/pm/Settings.java)).
  From the block it keeps `versionCode`, `minSdk`, `targetSdk`,
  `versionName`, `lastUpdateTime` and `installerPackageName`, and the
  lines it read. A package with no block is "not found at the stop", as
  an app uninstalled during the recording would be.
- **When adb fails.** A density or version not read is recorded with
  adb's reason, the collector reports itself degraded, and the recording
  goes on.
- **The player.** The "Checks" tab's first rows give the density, one
  row per period, such as "Phone density 480 dpi, so 1 dp is 3 pixels,
  the phone's own density, 00:00:00.000 to 00:01:00.793"; a recording
  made before step 2 shows "Phone density not recorded: the recording
  was made before the recorder read it." Each app's row gives its
  version, such as "App com.pss.novasleepcare, version 1.2.0 (code 34),
  targets API 35: 4 screens, 5 visits" (the numbers here are made up),
  or "version not recorded" for an older recording, "version not read"
  when adb failed, and "not found at the stop". The density and version
  records are on the timeline's settings strip, shown by the "Phone
  settings" filter.
- **The playback index** is version 27, so recordings indexed before are
  indexed again, and keeps the `device.android` records whole.

Measured on the Pixel 10 Pro XL (Android 17), 2026-11-12, in tests 7 to
9 of the validation plan:

- `wm density` printed "Physical density: 390", and with Display size
  one step larger also "Override density: 440".
- Nova SleepCare is version 1.0.5, version code 65, targeting API 36,
  matching the version on the phone's App info page; 3 of 3 packages
  seen were read.
- Display size one step smaller, back to the phone's own, wrote an empty
  `display_density_forced`, as the source says; one step larger wrote
  "440".
- Each Display size change gave the Settings page a new window, so a new
  visit to the same screen.

Measured before step 2, in the client's recording (session
`20261112-005243-5528d67531dd4441a50b2c2985350c59`): "Display size" was
not set (`display_density_forced` null) and the display mode was 1080 by
2404 pixels. The physical density was not recorded then.

## As built: step 3

Built 2026-11-12 in `src/Recorder.Session/PhoneTreeChecks.cs` (the
rules) and `src/Recorder.Session/PhoneChecks.cs` (running them on a
recording and merging the findings), with the player's part in
`src/Recorder.App/MainWindow.PhoneScreens.cs` and
`MainWindow.Touches.cs`, and tests in
`tests/Recorder.Tests/PhoneTreeChecksTests.cs` and
`PhoneCheckRunTests.cs`. Each rule was rewritten from ATF's source at
commit `c65cab0`; `PhoneTreeChecks.cs` carries ATF's Apache License 2.0
notice and lists what was changed.

- **What is checked.** For each visit, each settled snapshot's app
  window, with its items as the latest snapshot that took them gives
  them. The items one snapshot took are checked once in a visit, however
  many settled snapshots show them, and are one time a finding was seen,
  at the first of those. Only the app's own window is checked: a dialog
  or menu drawn in a window of its own is not yet checked, and a finding
  seen while one was over the app says so, such as "while dialog
  'Delete entry' was over it". A pane in the app's window is checked
  with the window, and its finding says "in pane 'Dialog'".
- **The density and the screen's size.** The touch target checks use
  the density in use when the items were taken (step 2), not when the
  snapshot listed them, and the screen's size in pixels at that time
  from the companion app's display records for the edge allowance. With
  no density recorded, as for recordings made before step 2, the touch
  target checks do not run, and the status line says on how many
  settled snapshots.
- **The density changing during a visit.** When the density changed
  after the visit began and before the items were taken, the touch
  target checks do not run on them, as they may have been laid out at
  either density, and the status line says on how many settled
  snapshots. Measured in test 9's recording: Display size was changed
  at 00:00:20.152; a snapshot at 00:00:20.223 still listed Settings'
  old window, with its items taken at 00:00:19.844 under the old
  density, and a new window replaced it at 00:00:20.42. The first run,
  which used the density at the snapshot's time, gave six false errors
  for buttons 48 dp wide measured at the new density.
- **Findings.** A finding is one check's result for one item on one
  visit at one level, with each time it was seen in the visit (decision
  8; until 2026-11-12 merged across the screen's visits):
  the snapshot, the item's bounds and row in the tree view, the values
  measured and the screen's state. An item is identified by its view ID
  with its place among items of that ID, or else by its class, text or
  content description, and the view IDs and classes of its ancestors, as
  "Items and findings" says.

ATF's behaviour kept as it is, each checked in ATF's source:

- **A clickable ancestor** makes an undersized target a warning when
  the ancestor is at least the needed size. ATF compares the ancestor's
  bounds in pixels with the needed size in dp
  (`TouchTargetSizeCheck.hasQualifyingClickableAncestor`), so an
  ancestor of 48 pixels counts as 48 dp. The recorder does the same, to
  give ATF's result.
- **Sizes are rounded** to whole dp as Java's `Math.round` does, halves
  up, before they are compared.
- **An item against the edge of a list that scrolls** is "not run", as
  it may be cut off by the list (`isAgainstScrollableEdge`, from the
  list's scroll forward and scroll backward actions). The recorder's
  24dp level does the same (inference: the same reason applies; added
  after the first run on test 9's recording gave an error for a row
  12 dp high cut off at the bottom of the Display size page).
- **Duplicate speakable text** is a warning on the first clickable item
  when one is clickable, and "info" on the first item when none is.

Where the recorder differs from ATF, as the recording does not hold
what ATF reads on the phone:

- **Touch delegates** and the size of a View before its parent clips it
  are not in the snapshot, so both are treated as absent. ATF would make
  some undersized targets a warning, or not run, with them.
- **Superclasses.** ATF loads each item's class on the phone to find its
  superclasses. The recorder has a fixed table of Android's own widgets
  from the Android API reference, and treats an app's own class as
  having none (inference: correct for the framework widgets listed; an
  app's own subclass of, say, `EditText` is not known to be one).
- **The words** of Redundant description are ATF's English words, as the
  phone's language is not recorded. ATF gives a result for each word;
  the recorder gives one per item naming every word.
- **The 24dp level** counted a target's own ancestors and descendants as
  the same place to touch, not as other targets (inference: WCAG 2.5.8
  speaks of separate targets). Replaced on 2026-11-16 (step 17): as WCAG
  counts overlapping targets as one only when they do the same thing,
  a target whose circle meets only them is a warning.
- **"Not run"** is listed only for the scrollable edge. ATF's other
  "not run" results (such as an item with no bounds) are left out, as
  they say nothing about the app.

The player:

- The checks run in the background when a recording is opened. Under
  each screen, after its visits, a row for each check that found
  something says how many findings it has at each level, such as
  "Speakable text present (ATF): 1 error", followed by a row for each
  finding, such as "Error: View with no text, a screen reader would
  focus it, but it has no text, content description, label from another
  item, or text from the items in it; seen once, at 00:00:14.354". A
  screen with none says "No findings from the checks of the tree on
  this screen."
- The status line gives the number of findings at each level, the
  settled snapshots checked and the time taken.
- Choosing a finding's row pauses playback, moves the playhead to the
  first time it was seen, selects its item in the "Accessibility tree"
  tab, and outlines the item on the phone's picture. The outline is set
  back 2 pixels of this PC's screen from the item, so the item's own edge
  shows, and is a cyan band 4 wide between black bands 2 wide, so it
  stands out on light and dark content; a side that would leave the
  picture is drawn just inside its edge. The first build drew it dashed,
  1.5 times the touch line's width, on the item's edge; on the client's
  app's logo, 59 by 58 pixels on the phone, it was about 12 pixels
  across on the PC and its dashes ran together (owner, 2026-11-12: "It
  needs to be thick and 2 color to make it stand out from the
  background, and 1-2 pixels pushed back from the item"). The
  accessibility focus's outline stays yellow over black on the item's
  edge. The outline is shown while the tree shows the
  items the finding was checked on, until the window's items are taken
  again. "Previous time" and "Next time" step through the other times.

Measured on the development machine, 2026-11-12, from the recordings'
files (not on the phone):

| Recording | Settled snapshots checked | Takings checked | Findings | Time |
|---|---|---|---|---|
| Client's app, `20261112-005243-5528d67531dd4441a50b2c2985350c59` | 11 | 10 | 4, touch target checks not run (no density) | 117 ms |
| Test 9, Display size, `20261112-031508-262e4d97af4847dca17d45d0131b4814` | 6 | 6 | 3, all not run (scrolling list's edge) | 151 ms |
| Test 64, Settings, `20261111-002648-06be83ce21ec47309bd4bc68b4ae1cb4` | 5 | 3 | 0 (no density) | 99 ms |

In the client's app, the findings were a View with no text a screen
reader would focus on "Sleep Learning", two items both speaking "2" on
the same screen (info, none clickable), a Button with no text in the
pane "Dialog" on "Sleep Diary", and the launcher's Google widget class.
In test 9, the findings were two rows and a button against the edge of
the Display size page's list that scrolls, each "not run". The page's
six preview and size buttons were 48 by 48 dp (117 pixels at 390 dpi),
so met ATF's 48; they had been reported as 43 by 43 dp before the rule
above was added. The times are from one run each and vary between runs
by some tens of milliseconds (measured: 156, 127 and 103 ms on the
first run).

## Step 4: measuring the video first

Step 4 starts with the measurement "Build order" asks for, before any
contrast check is built: how far the colours of the recorder's frames
are from the colours on the phone.

- **What the frames are.** The phone's picture is H.264 from scrcpy's
  server at its default 8 Mbit/s (`ScrcpyProtocol.VideoBitRate`),
  decoded and written as PNG only when a picture differs from the one
  before (`PhoneFrames.cs`); each 2 by 2 block of pixels shares one
  colour sample, used as it is with no smoothing
  (`PhonePicture.ToRgb`). So the frame is exact as a PNG of what was
  decoded, but not of what the phone drew (checked in the source).
- **What they are compared with.** Screenshots taken on the phone with
  `screencap -p` while the recording runs, which are lossless PNGs of
  the screen at the screen's size (inference from Android's
  `screencap`; the size is checked on the files). Each screenshot is
  matched to the frame of the same picture by its pixels, so no clock
  is needed; the screens are held still for several seconds so that
  each is in at least two screenshots.
- **What is measured.** For every item with text, and every image
  item, in the settled snapshot of the matched frame, ATF's colour
  swatch (`ContrastSwatch`, at commit `c65cab0`) is applied to the
  item's bounds in the frame and in the screenshot: the background and
  text colours each gives, and the two ratios. Also the share of
  pixels in the item that differ, and by how much.
- **What it decides.** Whether a ratio from the frame is close enough
  to the screenshot's to report, and with what margin, or whether the
  recorder should take a screenshot itself at each settled snapshot,
  which would make the contrast checks exact. The times between
  screenshots in the measurement show what a screenshot costs on the
  Pixel. The results and the choice go to the owner before the check is
  built.

### Step 4: what the measurement found (2026-11-12)

Test 16's recording, `20261112-154851-32c7b61a5552453bbf5febffcca978c7`,
of Nova SleepCare's Home, LEARN, DIARY and PROFILE tabs and Home again,
with 20 screenshots from `screencap -p`. All of this is measured on those
files unless it says otherwise.

- **Sizes.** The screenshots and the frames are both 1080 by 2404, so an
  item's bounds are the same pixels in both.
- **The whole picture.** Below the status bar, a held frame differs from
  its screenshot by 1.1 to 1.5 levels in 255 on average. Right after a
  change of screen the stream is still sharpening: on LEARN the frame at
  the settled snapshot, 22.602 s, differed by 4.6, and the frames 0.03 s
  later by 1.4. So the frame to measure is the last one before the next
  change of picture, not the first after the snapshot.
- **What ATF counts as text.** ATF's text contrast check runs only on
  items of the class TextView, or a subclass, with text or a hint
  (`TextContrastCheck.java`, checked in the source at `c65cab0`). In
  this app no item has text: every word on the screen is the content
  description of an `android.view.View`. ATF as written would check no
  text in it. For the measurement, every item with no children and a
  content description was taken as text, with every ImageView as an
  image: 125 items over 6 settled snapshots, 57 distinct.
- **ATF's colour swatch on the video as it is.** ATF's default, one
  text colour (`separateColorsUsingSingleForegroundMethod`), counts
  exact colours. On the screenshots an item has 2 to 3552 colours, on
  the frames 35 to 9878, as the stream turns each smooth edge into many
  near shades. For 109 of the 125 the ratios agreed within 10%, the
  median difference 1.3%; for 16 the swatch chose another colour as the
  text's, and the result would have been 13 false errors (for example
  "Sleep Goals, Update your goals": 2.86 from the frame, 12.40 from the
  screenshot), 1 false warning and 1 false pass (the badge "3": 13.64
  from the frame, 2.38 from the screenshot). ATF's other method, several
  text colours, found no second colour in most frames and gave 1.00.
- **The same swatch after joining near colours.** Joining colours
  closer than a Delta E of 3 (ATF's own measure, `colorDifference`),
  most common first, before the swatch: 122 of the 125 have the
  screenshot's level, with no false pass; the 3 left are one tile, an
  emoji with "Good" under it, where the frame's text colour came out as
  the emoji's. Of the 122, the frame's ratio was within 4% of the
  screenshot's for 90% of them, and within 11.2% for all; it was the
  lower in 25. With a Delta E of 10 all 125 had the screenshot's level.
- **What a screenshot costs.** Taking one and copying it to the PC took
  565 to 989 ms (`times.txt`), with adb started afresh each time. Whether
  taking one disturbs the phone's picture was not measured.
- **Limits.** One app, one light theme, five screens, held still, one
  phone. Dark themes, gradients, pictures behind text and motion were
  not in it.

### Step 4: the owner's decisions (2026-11-12)

1. **Where the colours come from: the phone's screenshot, else the
   video** (option C). The checks use a lossless screenshot of the
   screen when one was taken while the picture was the one the settled
   snapshot shows, and otherwise the video, with near colours joined
   and the finding marked approximate.
2. **Text is text, whatever ATF counts.** Any text seen on a background
   is checked for contrast, whether the app gives it as text, as a hint,
   as a content description, or not at all (text drawn in a picture).
3. **A setting for AA or AAA.** WCAG 2.2's 1.4.3, Contrast (Minimum),
   is level AA: 4.5:1, or 3:1 for large text; 1.4.6, Contrast
   (Enhanced), is level AAA: 7:1, or 4.5:1 for large text
   (https://www.w3.org/TR/WCAG22/#contrast-minimum,
   https://www.w3.org/TR/WCAG22/#contrast-enhanced).
4. **Non-text contrast as well**, a separate check (WCAG 2.2's 1.4.11,
   Non-text Contrast, level AA: 3:1 for the parts of controls needed to
   identify them and their states, and for parts of graphics needed to
   understand them, https://www.w3.org/TR/WCAG22/#non-text-contrast).
5. **OCR is used as well as the text the app gives, and every finding
   says when it was.** A finding whose text was found or placed by OCR
   says so, as does one whose text the app gave but whose place on the
   screen OCR found; a finding from the app's text and bounds alone
   says nothing about OCR.

### Step 4: the design

- **The phone's screenshots.** The companion is an accessibility
  service, and an accessibility service can take a screenshot of the
  display itself, `AccessibilityService.takeScreenshot`, once it
  declares `canTakeScreenshot` in its configuration; the screenshot is a
  `HardwareBuffer`, and calls closer than 333 ms apart are refused
  (`ACCESSIBILITY_TAKE_SCREENSHOT_REQUEST_INTERVAL_TIMES_MS`), and a
  window marked secure cannot be taken (`ERROR_TAKE_SCREENSHOT_SECURE_WINDOW`)
  (checked in AOSP's `AccessibilityService.java`). The companion takes
  one when the app's window has had no change for half a second, the
  same rule as a settled snapshot, and sends it as PNG with its time on
  the phone's clock over the link it already uses; the PC stores it with
  the recording beside the frames. A settled snapshot uses the first
  screenshot taken after it with no change of picture in the video
  between the two; if there is none (refused, secure, too soon, or a
  recording made before this), it uses the last frame before the next
  change of picture, as the measurement found the first frames after a
  change still sharpening. Turning `canTakeScreenshot` on changes the
  service's configuration, so the companion is installed again and, if
  Android asks, its service turned on again (inference; checked on the
  Pixel when built).
- **Finding the text.** Windows' own OCR (`Windows.Media.Ocr`) reads
  the screenshot or frame: it is part of Windows, runs on the PC, costs
  nothing, and gives the same answer for the same picture. On the PC it
  has English installed and read a whole phone screenshot in 72 ms,
  finding every line on the PROFILE screen, but reading "0" as "O"
  (measured with test 16's screenshot 13). So it is used for where text
  is drawn, not for what it says: an item's text, hint or content
  description is matched to the OCR lines inside its bounds, allowing
  for misread characters; an item whose description is not drawn (an
  icon's) is not text, and goes to the non-text check; a line inside no
  item's text, such as words in a picture, is checked as text in a
  picture.
- **Measuring a line.** The colours are measured in each OCR line's box,
  not the whole item, so an emoji or an icon beside the text is not
  taken for it (the 3 left over in the measurement were such a tile):
  ATF's swatch, one text colour, after joining colours closer than a
  Delta E of 3.
- **Text size.** Large text is 18 point, or 14 point bold
  (https://www.w3.org/TR/WCAG22/#dfn-large-scale); ATF takes that as 18
  or 14 bold in scaled pixels, the text's size divided by the scaled
  density (`isLargeText` and `ContrastUtils`, checked in the source at
  `c65cab0`), and the checks do the same. The app's size is
  used where Android gives it (`AccessibilityNodeInfo`'s rendering
  information, API 30, TextViews only); otherwise it is estimated from
  the line's height in pixels and the density, and the finding says
  so. Boldness is not known, so a ratio between the large and the normal
  limit is a warning, "fails unless the text is large", with the
  measured height.
- **Levels.** Below the limit for large text, an error; between the two
  limits, the warning above; from the video, a ratio within 12% of a
  limit is "near the limit", a warning, as the measured error was up to
  11.2%. The setting, AA or AAA, is in the Checks tab, kept between
  runs, and the findings are worked out again when it changes.
- **Non-text contrast (a later step).** What can be measured without
  judgement: an icon or image that is the only thing identifying a
  control, against its background (as ATF's `ImageContrastCheck`, 3:1);
  and the edge of a text field, a checkbox, a switch or a radio button
  against what is next to it, from a band of pixels just inside the
  item's bounds and one just outside. Whether a button's edge is needed
  to identify it cannot be told from the pixels (a button with text
  needs none), so those ratios are info, for review, not errors.
  Focus indicators are not in it, as they need the focus moved.

Build order: 4a, the companion's screenshots and their storage; 4b, the
colour measurement and the OCR matching, run on test 16's recording and
screenshots against the measurement above; 4c, the text contrast check
with the setting and its findings; then non-text contrast.

### As built: step 4a, the phone's screenshots

- **The companion app** (`Screenshots.java`, protocol 6) declares
  `canTakeScreenshot` and, for a connection that sends a "screenshots"
  request, takes a screenshot at once and another each time a window has
  had no change for half a second. A change is a window change, a
  content change or a scroll, counted against the window it came from,
  so a status bar clock does not hold up the app's window. Windows that
  settle while one is being taken get another as soon as it ends. Calls
  are kept at least 350 ms apart, above Android's 333 ms, and one refused
  as too soon is taken again; any other refusal is sent with Android's
  code. The screenshot is copied out of Android's hardware buffer,
  converted to sRGB if Android gave another colour space (it says which
  it gave), written as PNG on its own thread and sent from another, so
  the events and the other messages do not wait behind it. The PNG goes
  in the line as base64, put in by hand, as Android's `org.json` writes
  each "/" as "\/". Nothing is kept on the phone.
- **The time.** Android's time for a screenshot,
  `ScreenshotResult.getTimestamp()`, is `SystemClock.uptimeMillis()`
  (checked in AOSP's `AccessibilityService.java`), the same clock as the
  companion's `System.nanoTime()` (inference, as for the events' times,
  which the recorder already places this way); the companion also sends
  its own times for asking, for Android's answer and for the PNG. The
  recorder uses Android's time when it falls within the call, to the
  millisecond, and otherwise the middle of the call, and the record says
  which.
- **The recorder** has a new collector, `android.screenshots`, with its
  line under "Collector status". It writes each screenshot as
  `screenshots/android/NNNNNNNNNN.png`, numbered from 1, checks that the
  PNG's header has the size the companion gave, gives its SHA-256 to the
  manifest, and records it on `graphics.android.screenshots`
  (`android-screenshot`), with refusals (`android-screenshot-refused`,
  with Android's code in words) and what could not be read or was
  missing. The records are on the player's Frames lane and filter. A
  recording made before this, or with an older companion app, has none,
  and the checks use the video as decided. No database table was added:
  a recording with a recording file does not fill the evidence tables,
  as for the tree snapshots.
- **Checked before the Pixel tests:** the solution builds with no
  warnings, the companion app compiles with warnings as errors and
  converts to Android byte code, and the tests pass, 2,309 of them, with
  the 10 that need Windows failing as before. The new tests cover the
  request, reading a screenshot and a refusal, a PNG of the wrong size,
  the placing by Android's time and by the call, a missing one, no phone
  clock, no companion connection, and the connection ending. None of
  that shows Android taking them on the Pixel; tests 17 and 18 of the
  validation plan do.

### As built: step 4b, measuring the colours and finding the text

Step 4b measures; it reports nothing yet, and nothing in the recorder's
window changes. Step 4c turns the measurements into findings.

- **The screens measured** (`PhoneContrastRun`). For each visit, each
  settled snapshot whose items for the app's window were taken by a
  snapshot not already measured in the visit, so a screen is measured
  once for each time its items were read.
- **The picture.** The first of the companion's screenshots taken from
  the settled snapshot until the next snapshot that lists the app's
  window, or the visit's end; if there is none, the last frame of the
  video before then, if it is in the visit; if there is neither, the run
  says so for that snapshot. This is not quite the design's rule, "no
  change of picture in the video between the two": the video's picture
  also changes when only the status bar's clock does (measured in test
  18's screenshots 4 and 5), so the tree is used instead, as the
  companion takes a snapshot after each change in the app's window
  (inference from the step 7b rules).
- **The screenshots in the player's index.** A recording read from its
  recording file keeps only some records in memory, in the playback
  index; the screenshot records were not among them, so the first run on
  test 18's recording found none. The index, now version 28, keeps them
  with their paths (`SessionPlaybackArchive.PhoneScreenshots`), and an
  older index is worked out again when a recording is opened.
- **The colours** (`PhoneColours`). ATF's swatch with one text colour,
  as in the measurement: the colours are counted exactly, those closer
  than a Delta E of 3 joined, most common first, into the first kept
  colour (ATF's `colorDifference`), then the most common colour above
  the average luminance of the colours and the most common below it are
  the two, the more common of them the background, a tie going to the
  darker, as in ATF; the ratio is rounded to two places. The same
  joining is used on screenshots: on test 16's screenshots it changed no
  ratio (measured, 110 lines).
- **Reading the text.** `WindowsPhoneTextReader` uses Windows' OCR
  (`Windows.Media.Ocr`) for the user's languages, and gives its words
  with their boxes and lines and the engine's name and language. For
  this step the words were read on the PC by PowerShell with the same
  calls and saved; the reader itself is first run on the PC in step 4c.
- **Matching the words to the items** (`PhoneTextMatching`). A word
  goes to the smallest item its middle is in whose text, hint or
  content description has it: spelt the same, allowing "0" for "o"; "1",
  "i", "|" and "!" for "l"; and "5" for "s"; within one letter for a
  word of four letters or more and two for eight or more; or as part of
  the item's text run together, as OCR joins and splits words. The words
  of one OCR line that go to one item are one line to measure, so a row
  of tiles read as one line is measured tile by tile. A word in no
  item's text is text OCR found if it has at least two letters or
  digits. A word whose middle is outside the app's window, or in a
  window above it such as the status bar, is not the app's.
- **The box measured** is the line's words, widened by a fifth of its
  height on each side, at least 2 pixels, but not past the item it is
  in, so there is background around the letters.
- **Text OCR did not read.** An item with text of which OCR placed
  nothing is measured on its bounds if it is not an image, has no
  children, and is not cut off by a list that scrolls (ATF's "against a
  scrollable edge"). That last rule was added after the measurement
  below: two items at the bottom of a list, mostly scrolled out of
  sight, were measured on the slivers left, which showed a border and
  no text.
- **Pictures smaller than the screen.** The tree's bounds are in the
  screen's pixels; a picture of another size is measured with the
  bounds scaled to it.

Measured with test 16's recording and 20 screenshots taken with
`screencap -p` (as in "Step 4: what the measurement found"), and test
18's recording:

- **Test 16, from the video.** 5 settled snapshots, each measured on a
  frame, as the recording has no companion screenshots: 110 lines, 105
  placed by OCR and 5 on an item's bounds, none found by OCR alone.
  Each frame was matched to its screenshot by its pixels (1.1 to 1.3
  levels in 255 apart on average below the status bar) and the same
  boxes measured on the screenshot. All 110 have the screenshot's level
  (against 3:1 and 4.5:1); the ratios differ by a median of 0.9%, by
  4% or less for 106, and by 6.5% at most, against 11.2% when the whole
  item was measured; the frame's was the lower in 17. On the screenshots
  99 pass 4.5:1, 6 are between 3:1 and 4.5:1, and 5 are below 3:1.
- **The emoji tiles** that came out wrong in the measurement ("Good"
  under an emoji) are now measured on their words alone and have the
  screenshot's level.
- **The same as ATF.** Measured without joining, the swatch on the
  screenshots gives the same ratio as a separate re-implementation of
  ATF's for all 110 boxes.
- **Test 18, from the phone's screenshots.** 3 settled snapshots, each
  measured on a companion screenshot taken 9, 529 and 518 ms after it.
- **Checked before any Pixel test:** the solution builds with no
  warnings and the tests pass, 2,330 of them, with the 10 that need
  Windows failing as before. The new tests cover the swatch (one colour,
  two, a tie, rounding, joining, the Delta E, a box outside the
  picture), reading a PNG, the matching (the smallest item, tiles, a
  near misspelling, the status bar and the window's edge, text OCR
  found, the bounds rule and its exceptions, a smaller picture), the
  choice of picture, the next snapshot listing a window, and the
  screenshots in the index. There is no Pixel test for 4b, as nothing
  on the screen changes; step 4c's tests check the findings on the
  phone.
- **Limits.** As in the measurement: one app, one light theme, five
  screens, held still, one phone, and one OCR language.

### As built: step 4c, the text contrast check

Step 4c turns step 4b's measurements into findings in the Checks tab.

- **When it runs.** When a phone recording is opened, after the checks
  of the tree, in the background: step 4b's measurement, with Windows'
  OCR (`WindowsPhoneTextReader`) reading each picture once, then the
  check (`PhoneTextContrastRun`). If Windows has no OCR language for the
  user, or OCR cannot be started, the app's text is measured on its
  items' bounds only, and the tab says so.
- **The setting.** "Text contrast level:" in the Checks tab, with "AA
  (WCAG 1.4.3)" and "AAA (WCAG 1.4.6)", kept per Windows user in
  `check-settings.json` beside the player's layout (`PhoneCheckSettings`);
  a missing or unreadable file is AA. It is shown as the window opens, and
  can be changed before a recording is open. Changing it works the findings out
  again from the same measurement, without measuring or reading again.
  Since 2026-11-12 this setting is the general audit level (see "The
  audit level"), and a file holding only the old "contrastLevel" keeps
  its level. The check is named for the level: "Text contrast at AA (WCAG 1.4.3)"
  or "Text contrast at AAA (WCAG 1.4.6)".
- **The limits.** AA: 4.5:1, or 3:1 for large text; AAA: 7:1, or 4.5:1
  for large text (decision 3). The ratio compared is worked out again
  from the two colours, unrounded, as WCAG's limits are minimums and
  WCAG says a ratio is not rounded to meet one ("Contrast (Minimum)",
  https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html);
  ATF lets a ratio fall 0.01 below its limit (`CONTRAST_TOLERANCE` in
  `TextContrastCheck`, checked in the source at `c65cab0`), which is
  not followed, as decided ("Text is text, whatever ATF counts"). A
  ratio is shown to two places, or three when two would round it up to
  a limit it is below, such as 4.496:1.
- **The text's size: not known.** The design used the app's size where
  Android gives it, but the companion does not send `AccessibilityNodeInfo`'s
  rendering information, so the size is never known from the app; adding
  it is a change to the companion, left for later. The height of OCR's
  box round the words is given in dp (pixels over the density when the
  items were taken), but it runs from the tallest letter to the lowest,
  and the font is not known, so it is not taken as the text's size, and
  boldness is not known either. So the size decides nothing: a line
  below the large text limit is an error, and one between the two
  limits is a warning, "fails unless the text is large", with the words'
  height to help the person reviewing it decide.
- **From the video.** A frame's ratio could be up to 12% higher or lower
  than the screenshot's would be (`VideoMargin`; the measurement found
  11.2% at most on whole items, and step 4b 6.5% on OCR's lines), so the
  level is the one the highest ratio it could be would have, and a line
  it could put on the other side of a limit says so: "near the 4.5:1
  limit, so it may fail, as the video's colours are approximate" for a
  ratio that would pass, and "and it may be below 3:1" added to the
  warning for one between the limits. An error from the video is below
  the large text limit even 12% higher.
- **One colour.** A box with one colour after joining has no text to
  measure in it; it is "not run", saying so, not a 1:1 error.
- **What a finding says**, after its level and label: the ratio and the
  colours, the verdict, the words' height or "the text's size and
  weight are not known" for a line on an item's bounds, "from the
  phone's screenshot" or "approximate, from the video", and how the text
  was found (decision 5): "the app's text, placed on the screen by OCR
  (Windows OCR, en-US)", "text found by OCR (Windows OCR, en-US), in no
  item's text", or, with nothing about OCR, "measured on the item's
  bounds". For example, from test 16's video: "Warning: View described
  as "RECENT SLEEP ENTRIES", 3.51:1, #798587 on #F7F7E3; fails unless
  the text is large: 4.5:1 is needed, 3:1 for large text; the words are
  11.1 dp tall in OCR's box; the text's size and weight are not known;
  approximate, from the video; the app's text, placed on the screen by
  OCR (Windows OCR, en-US); seen once, at 00:00:42.154".
- **Which line a finding is.** As a tree check's finding is one item's,
  a contrast finding is one line's, merged in each visit by level and
  line across the snapshots measured (decision 8): an item's line is the item's
  identity and the line's number from the top, the label naming the
  line ("line 2 of 2, 'Update your goals'") when the item has more than
  one; text found by OCR is the item it is in and the words as matched
  (`PhoneTextMatching.Spelling`), labelled with OCR's words, so the same
  words read differently by OCR at another time are another finding.
- **The rows.** Under each visit, after its tree findings, a row for
  the check with how many findings at each level and how many lines
  were measured in the visit, or that it found nothing, or that no text
  was measured in the visit; then a row for each finding. A visit with
  no settled snapshot has one row, "Not checked, as no snapshot of this
  visit settled.", as both checks run only on settled snapshots. Choosing one
  moves the playhead to the picture the line was measured on, selects
  the item, or the
  item the words are in, in the "Accessibility tree" tab, and outlines
  the line's box, not the whole item, on the phone's picture. The
  status line under the buttons adds the check's totals: findings, lines
  measured, how many passed, the settled snapshots measured and how
  many from screenshots and from the video, the time taken, and the OCR
  engine.
- **A finding's times are its pictures'.** Each time a contrast finding
  was seen is the time of the screenshot or frame measured, not the
  time the snapshot settled, so the player shows the picture the
  colours came from. In test 16's recording the first visit's snapshot
  settled at 00:00:01.608, before the video's first frame at
  00:00:01.651, so the player showed no picture there (found by the
  owner after test 22); the frame measured was at 00:00:02.655. The
  frame is the last before the window was next listed, which can be
  late in the visit: "RECENT SLEEP ENTRIES" is at 00:00:42.154, in a
  visit that ends at 00:00:42.155. For each of the 8 snapshots of tests
  16 and 18, the items the snapshot took are still the latest at the
  picture's time, so the outline is drawn there.

Run on the development PC, with test 16's recording and the words
Windows' OCR read on the PC for step 4b (not yet the reader in the
recorder):

- **At AA**, 110 lines on 5 settled snapshots, all from the video: 99
  pass; 4 errors, the badge "3" (2.41:1, on 2 visits, so, since
  decision 8, 2 findings) and "0" (1.55:1), "0%" (1.98:1) and "0.0" (1.94:1) on the
  PROFILE screen; 6 warnings, the grey second lines of the PROFILE
  screen's 5 buttons (3.81:1 to 3.83:1) and "RECENT SLEEP ENTRIES"
  (3.51:1). These are the lines step 4b found below 3:1 and between the
  limits on the screenshots, so the 12% margin moved none of them. Making
  the findings took 17 ms; the measurement 1.2 seconds, without OCR.
- **At AAA**, the same lines: 98 pass; the 10 above are errors, and
  "WEEK VIEW" (6.20:1) is a warning, between 4.5:1 and 7:1.
- **Test 18's recording**, with the words Windows' OCR read on the PC
  from its 5 screenshots: 57 lines on 3 settled snapshots, all from the
  phone's screenshots, 55 pass; the badge "3" (2.38:1) is an error, and
  "RECENT SLEEP ENTRIES" (3.49:1) a warning. Without OCR the same
  snapshots give 43 lines, and the emoji tiles "Terrible" and "Poor",
  measured on the tiles' bounds with the emoji, are warnings too, as in
  the measurement; with OCR they are measured on their words and pass.
- **Checked before any Pixel test:** the solution builds with no
  warnings, and the tests pass, 2,347 of them, with the 10 that need
  Windows failing as before. The new tests cover the levels at AA and AAA from a
  screenshot, the unrounded ratio and its third place, the video's
  margin on both sides of each limit, one colour, the wording of where
  the colours and the text came from, the words' height without a
  density, the setting kept and an unreadable one, the words' height
  from the matching, and the findings merged across snapshots at each
  level.
- **Limits.** As in step 4b; and the reader in the recorder, and the
  setting and the rows, are first checked on the PC in tests 19 to 22
  of the validation plan.

## Step 5: measuring non-text contrast first

The owner chose on 2026-11-12 to measure the recordings before deciding
the rules for non-text contrast, as was done for text contrast. WCAG
2.2's 1.4.11, Non-text Contrast, is level AA only, with no AAA level, so
the AA or AAA setting does not apply to it.

### What 1.4.11 asks (checked in a cited document)

From W3C's Understanding document
(https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html):

- 3:1 against adjacent colours for the visual information required to
  identify user interface components and their states, and for the parts
  of graphics required to understand the content. Inactive components
  are excepted. The ratio is not rounded: 2.999:1 does not meet 3:1.
- A control needs a visible boundary only when nothing else shows that
  it is there: "If a control has visible content (such as text or a
  sufficiently contrasting icon) ... a border or other indication of the
  overall boundary of the hit area is not required".
- Visual information that indicates a state, such as selected, has to
  meet 3:1 with the adjacent colours. But "the change of background
  within the component is not in scope of non-text contrast. However,
  this would not pass Use of color" (Figure 15). Pale yellow stars on
  white, 1.2:1, fail (Figure 7).
- Colours should be taken from the source, not from the anti-aliased
  rendering, which makes thin lines fainter.
- Focus indicators have to contrast too; they need the focus moved,
  which these recordings do not do (TalkBack is off in them).

### How it was measured

A throwaway program on the development PC (not in the repository) ran
the recorder's own code on each settled snapshot of test 16's recording
(5 screens, measured on the video frames and on the phone's lossless
screenshots taken at the same moments) and test 18's (3 settled
snapshots, the phone's screenshots). For every visible item that is
clickable, checkable or editable, or an image: on each side, along its
middle half (to miss rounded corners), the adjacent colour is the
commonest colour 5 to 8 pixels outside the bounds; scanning from 4
pixels outside to 16 inside, the first colour 3 or more Delta E from it
starts the drawn shape (a border or a fill), and the shape's colour is
the one of highest contrast with the adjacent colour in that pixel and
the next 3, so that the anti-aliased edge is passed over. The pictures
were then looked at to check what the numbers meant.

### What was found (measured, test 16 session 20261112-154851-32c7b61a5552453bbf5febffcca978c7, test 18 session 20261112-171107-88cfc05214a34a3682222e7fb25d840d)

- **The tree.** 38 items on 4 of test 16's 5 screens (the loading
  screen has none; 50 counting both Welcome visits), 28 on test 18's 3. No item has text of its own: all the words
  are in content descriptions (as Flutter gives them; see step 10). No item is checkable, and none
  is marked selected: the bottom tabs and the CALENDAR and WEEK VIEW pair
  have no selected state in the tree. There are no items for the icons,
  the chevrons, the progress bars on Learn, the weekly recap chart or
  the illustrations; the Learn cards are each one image item.
- **Drawn boundaries that meet 3:1.** The Learn cards, the Good morning
  card, the CALENDAR and WEEK VIEW pair and the Profile screen's list
  buttons have a 2 to 3 pixel border, #003B3D, on the item's edge
  against the page (#F6F6E2), the list (#FFFFFF) or a progress track
  (#DDE0BC): 9.13:1 to 13.21:1.
  OPEN BEDTIME MODE is white on the dark card, 12.4:1. Sleep Goals'
  outline, #A6BABB, is drawn 6 pixels inside its bounds, 6.12:1 against
  the dark card.
- **Controls with text and a pale boundary (not required by 1.4.11).**
  The emoji tiles, #E25825, #4A90D9, #A2D297, #F1AE53 and #F3CB51 on the
  page, are 3.39:1, 3.06:1, 1.57:1, 1.76:1 and 1.43:1; each has its word
  under it. The "Start your streak" card's edge is 1.21:1. These have
  visible text, so their boundaries are not required.
- **The selected tab.** The bottom bar shows which tab is selected only
  by a pale pill, #F5E19A on the bar's #F3CB51: 1.2:1 on every visit
  (HOME, LEARN, DIARY, PROFILE). The other tabs have no shape. The tab's
  icon and word, black, are 13.47:1 on the bar and 16.12:1 on the pill.
- **The selected segment.** WEEK VIEW, selected, is filled #81C4C1, and
  CALENDAR is white: 1.98:1 between them, 1.81:1 against the page; the
  pair's border is 11.34:1.
- **Icons.** The bell is black on the bar's yellow, 13.47:1; its badge,
  #E25825, is 2.38:1 on the bar (its "3" is text and already a text
  contrast finding). ATF's way of measuring an image, one foreground
  against the commonest colour, gives the bell button 2.38:1, because
  the badge's orange outweighs the bell's black; measuring the icon
  needs the most contrasting colour with enough pixels instead.
- **Progress bars on Learn** (picture only, not in the tree): the fill,
  #003B3D, is 9.13:1 on the track, #DDE0BC, and the track is 1.24:1 on
  the page, so the bar's full length is hard to see.
- **The video against the screenshots.** The smallest ratio of each
  item's sides from the video was within 7.3% of the screenshot's (most
  within 2%), inside the 12% margin used for text.

### Questions for the owner

1. Which controls need a boundary: only those with no visible text and
   no icon of 3:1 or more (as 1.4.11 says), with every other boundary
   below 3:1 noted as info or not listed?
2. A selected state shown only by a pale fill or pill below 3:1 (the
   tabs, 1.2:1; WEEK VIEW, 1.98:1): an error under 1.4.11 (as a state
   indicator, like Figure 7), or a note that it is out of 1.4.11's scope
   (Figure 15) but fails Use of color (1.4.1)? The tree does not say
   which tab is selected, so it would be found from the pictures: among
   controls of one class and size in a row, the one drawn differently.
3. Graphics with no item in the tree (progress bars, charts, icons
   inside cards): left out, found from the pictures by fixed rules, or
   left for the vision model?
4. How an icon is measured: the most contrasting colour inside the
   control with at least a set number of pixels, against the control's
   background?

### Step 5: the owner's decisions (2026-11-12)

1. **Boundaries.** A control needs a boundary of 3:1 only when it has no
   visible text and no icon of 3:1 or more, as 1.4.11 says. Every other
   boundary below 3:1 is listed as info, so it can be seen, not as a
   failure (owner: "Same, but list others as info").
2. **A state shown only by a pale fill is an error under 1.4.11**
   (owner). The selected tab's pill, 1.2:1, and WEEK VIEW's fill, 1.98:1,
   are errors.
3. **Graphics with no item in the tree are left for the vision model**
   (owner): progress bars, charts, illustrations and icons drawn inside
   cards are not checked by this check.
4. **How an icon is measured** (inference, chosen when building, as the
   owner did not answer question 4 separately): the colours inside the
   control, 2 pixels in from its bounds, are counted, with colours less
   than 3 Delta E apart joined; the control's fill is the commonest, and
   its icon is the colour of highest contrast with the fill that covers
   at least 0.5% of the control and at least 9 pixels. This is what found
   the bell black, 13.47:1, where ATF's way gave 2.38:1.

### As built: step 5, the non-text contrast check

- **Files.** `PhoneNonTextContrast.cs` in `Recorder.Session` measures
  the controls (`PhoneControlMeasuring`) and judges them
  (`PhoneNonTextContrastRun`). The measurement is made with step 4b's,
  on the same settled snapshots and the same pictures, and kept with
  each snapshot (`PhoneContrastSnapshot.Controls`), so the pictures are
  read once.
- **What is measured.** Each item of the app's window that is visible,
  enabled, not empty, and clickable, long-clickable, checkable or
  editable. Inactive controls are left out, as 1.4.11 excepts them.
- **Each side.** As in the measurement: along the side's middle half,
  the adjacent colour is the commonest 5 to 8 pixels outside the bounds;
  from 4 pixels outside to 16 inside, the first colour 3 or more Delta E
  from it starts the drawn shape, whose colour is the one of highest
  contrast in that pixel and the next 3. If nothing is drawn, the side
  has a ratio of 1:1 ("no boundary is drawn"). A side is not measured
  when the pixels outside it are not in the app's window, are under a
  window above it (a dialog or the keyboard), or are where a list cuts
  the control off (the edge of a scrollable item it is in).
- **Visible text.** A line OCR read whose middle is inside the control;
  without OCR, the control's text or description when it has a letter
  or digit. The finding says which.
- **The rules.** A control with visible text, or with an icon of 3:1 or
  more, needs no boundary: a boundary below 3:1 is info (decision 1). A
  control with neither needs an icon or a boundary of 3:1: below it is
  an error. A state (decision 2) is found from the tree when it marks
  exactly one control of a group selected or checked; otherwise from
  the picture, among controls of one parent and class in a row (the
  same top and height, within 10%): the one filled differently from all
  the others, or, of two that touch, the one less like the colour round
  them. Its fill below 3:1 against the others' fill or the colour around
  it is an error. Controls stacked in a column (cards) are not taken to
  show a state. From the video, a ratio within 12% of 3:1 is a warning,
  as for text. The ratio is compared unrounded, and shown to three
  places when two would round it up to 3:1.
  Text fields, and text OCR read with no word in the item's text in the
  tree, were judged differently from 2026-11-16 (see "Step 18:
  boundaries without a model").
  From 2026-11-19, a state the tree marks counts only when its fill
  differs from the others' (within the swatch's join limit); a control
  filled like the others is judged as any control is (see step 29, "The
  owner's decision on the selected tabs (2026-11-19)").
- **Findings.** Under each visit, as for text, with the controls
  measured in it; and in the summary line, "at AA, its only level", with
  "graphics with no item in the tree are not checked".
- **Run on the development PC** with tests 16 and 18's recordings and
  the words Windows' OCR read on the PC (measured, test 16 session
  20261112-154851-32c7b61a5552453bbf5febffcca978c7, test 18 session
  20261112-171107-88cfc05214a34a3682222e7fb25d840d):
  - Test 16, from the video: 50 controls, 13 passed, 37 findings, 6
    errors and 31 info, made in 26 ms. The errors are the selected tab
    on each visit, HOME (visits 1 and 7), LEARN (visit 2), DIARY (visit
    4) and PROFILE (visit 6), filled #F6E19B on the bar's #F5CC53,
    1.18:1, and WEEK VIEW (visit 4), #82C4C0, 1.98:1 against CALENDAR's
    white and 1.83:1 against the page. The info are the other tabs (no
    boundary), the emoji tiles, Sleep Goals, the bell (its icon 13.64:1,
    no boundary), "Start your streak" (1.21:1) and the Emotion card. The
    loading screen's visits have no settled snapshot, so no control.
  - Test 18, from the phone's screenshots: 28 controls, 6 passed, 22
    findings, 5 errors: the selected tab, 1.20:1 (#F5E19A on #F3CB51),
    on each of the 3 visits, and WEEK VIEW, 1.98:1 and 1.81:1 (#81C4C1).
- **Checked before any PC test:** the solution builds with no warnings,
  and the tests pass, 2,363 of them, with the 10 that need Windows
  failing as before. The new tests cover a border found on the edge,
  the selected tab from the picture and from the tree, two segments that
  touch, cards in a column and two apart, a control with no text needing
  an icon or a boundary, the video's margin, inactive controls and a
  side cut off by a list, and the findings merged across snapshots.
- **Limits.** The drawn shape has to be within 16 pixels of the bounds,
  so a shape drawn further in, or bounds that include a gap (the Learn
  cards' bounds include the progress bar under them), are not measured
  as the shape. Graphics with no item are not checked (decision 3).
  Focus indicators are not checked, as they need the focus moved. A
  state is found only in rows of controls; one shown by an outline, an
  underline or a change of the icon, not a fill, is not found.

## Step 6: titles and headings

### What the standards ask (checked in cited documents)

- **The checks are of an app, not a web page**, so the requirements are
  read through WCAG2ICT, the W3C's guidance on applying WCAG 2.2 to
  non-web software ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)).
- **1.3.1 Info and Relationships applies as written.** WCAG2ICT's note
  2 says information is programmatically determined in software through
  the platform's accessibility services
  ([WCAG2ICT, 1.3.1](https://www.w3.org/TR/wcag2ict-22/#sc1-3-1-info-and-relationships)).
  The W3C's Understanding document for 1.3.1 does not itself require a
  heading on every page
  ([Understanding 1.3.1](https://www.w3.org/WAI/WCAG22/Understanding/info-and-relationships.html));
  the "no heading" rule below is the owner's reading of it.
- **2.4.2 Page Titled becomes "Non-web Software Titled"**: "In non-web
  software implemented on a platform that supports a Title property for
  windows or screens, the non-web software provides titles that
  describe the name, topic or purpose of each window or screen"
  ([WCAG2ICT, 2.4.2](https://www.w3.org/TR/wcag2ict-22/#sc2-4-2-page-titled)).
- **Android's title properties** are the window's title and the pane
  title, `accessibilityPaneTitle` in views and `paneTitle` in Compose,
  and Android asks for "unique pane titles across your app"
  ([Android, principles](https://developer.android.com/guide/topics/ui/accessibility/principles),
  [views](https://developer.android.com/guide/topics/ui/accessibility/views/principles-views),
  [Compose semantics](https://developer.android.com/develop/ui/compose/accessibility/semantics)).
- **Android's headings have no levels**: an item is a heading or not
  (`isHeading`,
  [AccessibilityNodeInfoCompat](https://developer.android.com/reference/androidx/core/view/accessibility/AccessibilityNodeInfoCompat)).
  The companion records it for every item (checked in source:
  `AccessibilityEventJson.java`, `item.put("heading", node.isHeading())`).

### The owner's decisions (2026-11-12)

1. **Each issue is its own finding.** "Always break issues down into
   their atomic parts. No window or pane title is one discrete issue.
   duplicate pane title is another. It allows remediation advice to be
   more specific" (owner).
2. **No title is an error, under WCAG2ICT's 2.4.2.**
3. **A title shared with other screens is a separate error, under the
   same criterion.**
4. **No heading on the screen is an error under 1.3.1.** "The page needs
   to be described properly to AT and that means the structure of the
   page must be evident. Without headings you just have prose. ... It is
   a reasonable argument to say that without at least one heading, the
   page fails to be described adequately. That does allow for some false
   positives (potentially e-book pages for example) and I'm okay with
   that" (owner).
5. **A first heading not close to the top of the screen is a warning.**
6. **An abundance of headings on the screen is a warning.**

### Measured first (2026-11-12)

The recordings to hand were run through the new checks on the
development PC before the limits were chosen (measured: test 16 session
20261112-154851-32c7b61a5552453bbf5febffcca978c7, test 18 session
20261112-171107-88cfc05214a34a3682222e7fb25d840d, and the earlier
recordings of the client's app and of Settings):

- **The client's app.** Every screen's window title is "Nova SleepCare";
  its one pane's title is a single space; no item on any screen is
  marked as a heading; while the diary's dialog is open the pane's title
  is "Dialog". So each checked visit gets two errors, the shared title
  and no heading, and none for a missing title.
- **Settings.** "Safety and emergency" and "Display size & text" have
  their title marked as a heading at 59 to 66 dp from the top of the
  window, 7% of its height; "Display size & text" has 3 headings shown
  against 15 to 17 items a screen reader stops at. Settings' first screen
  has 6 items marked as headings, none shown to the user (not visible,
  some with no height), so it gets "no heading". The launcher's home
  screen has no heading.
- **No screen measured has a first heading below its top quarter or
  more headings than half its screen reader's stops**, so the two
  warnings below are set from the unit tests' made-up screens, not from a
  recording.

### As built: step 6, the title and heading checks

- **Files.** `PhoneScreenChecks.cs` in `Recorder.Session` runs the
  checks of one window; `PhoneCheckRun` runs them with the tree's
  checks on each visit's settled snapshots, on the app's window, and
  then checks the shared titles across the recording's screens. The
  window's title is now passed with its items (`PhoneCheckWindow.Title`).
  The findings are listed under each visit with the tree's, after them.
- **The screen's title** is the first non-blank pane title in the
  tree's order, or else the window's title; a title of only spaces is
  none, as nothing would be said.
- **No screen title (WCAG2ICT 2.4.2)**, an error on the window's first
  item, when the screen has no title; it says whether the window's title
  is missing or blank and whether its pane titles are blank.
- **Screen title shared with other screens (WCAG2ICT 2.4.2)**, an error
  on the window's first item, for each visit whose screen's title is
  also the title of another screen of the same app checked in the
  recording, ignoring case. It says which title (pane or window) and
  names up to 3 of the other screens.
- **No heading (WCAG 1.3.1)**, an error on the window's first item, when
  no item shown is marked as a heading. An item marked as a heading is
  shown when it is visible to the user and has an area; the finding
  counts those that are not shown.
- **First heading far from the top**, a warning on the topmost heading
  shown, when it starts below the top quarter of the window's height
  (my assumption for "close to the top"; the measured titles start at
  7%). It says how far down, in dp and as a share of the height.
- **Many headings**, a warning on the window's first item, when at
  least 3 items shown are headings and they are more than half as many
  as the items a screen reader stops at (ATF's `shouldFocusView`, as
  for Speakable text present). This is my assumption for "an
  abundance"; it is meant to catch every item of a list marked as a
  heading, not cards with a heading each.
- **Run on the development PC** with test 16's recording: on each of
  the 5 checked visits, "Screen title shared with other screens", "its
  window title 'Nova SleepCare' is also the title of 3 other screens
  checked in this recording", and "No heading". The loading screen's
  visits have no settled snapshot, so they are not checked and not
  counted among the screens sharing the title. Test 18's recording gives
  the same two errors on each of its 3 visits, with 2 other screens.
- **Checked before any PC test:** the solution builds with no warnings,
  and the tests pass, 2,377 of them, with the 10 that need Windows
  failing as before. The new tests cover a missing and a blank title, a
  pane title before the window's, two screens sharing a window title
  (each visit's own error, apart from "No heading"), headings not shown,
  the first heading in and below the top quarter, with and without the
  density, and the limits for many headings.
- **Limits.** Whether a unique title describes the screen is for a
  person or a vision model; the check finds only missing and shared
  titles. A title is compared only with the screens seen in the same
  recording. A screen scrolled so that its headings are off the screen
  gets "no heading", and one scrolled past its title gets "first heading
  far from the top", as the checks are of what is on the screen. A
  heading with no text of its own or its children's is counted as a
  heading.

## The audit level

### The owner's decision (2026-11-12)

Asked whether the touch target checks follow the AA or AAA setting (24
or 44 px), the owner decided: "I want a general AA / AAA "audit" flag to
guide checks and check reporting". The proposed units (dp for CSS
pixels) and the standard (WCAG 2.5.5 applied directly) were accepted.

### What the standards ask (checked in cited documents)

- **WCAG 2.5.5 Target Size (Enhanced), level AAA:** "The size of the
  target for pointer inputs is at least 44 by 44 CSS pixels except
  when" the target has an equivalent of that size on the same page, is
  inline in a sentence or block of text, is sized by the user agent and
  not changed by the author, or a particular presentation is essential
  ([Understanding SC 2.5.5](https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced.html)).
  Unlike 2.5.8, it has no spacing exception.
- **WCAG2ICT** gives guidance on applying the level A and AA criteria to
  non-web software, not AAA
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)), so 2.5.5 is applied
  directly, with Android's dp for CSS pixels, as the 24dp check of 2.5.8
  already does.

### As built

- **The setting.** "Audit level:" in the Checks tab, with "AA" and
  "AAA", in place of "Text contrast level:". It is kept in
  `check-settings.json` as "auditLevel" (`PhoneCheckSettings.AuditLevel`,
  the `PhoneAuditLevel` enumeration, renamed from `PhoneContrastLevel`);
  a file with only the old "contrastLevel" keeps that level, and a
  missing, unreadable or unknown level is AA.
- **What it guides.** AA: text contrast to WCAG 1.4.3 and target size
  to 2.5.8 (the ATF 48dp check is run at both levels). AAA: text
  contrast to 1.4.6 in place of 1.4.3, and target size to 2.5.5 as well
  as 2.5.8. Non-text contrast (1.4.11) and the title and heading checks
  are the same at both levels.
- **Touch target under 44dp (WCAG 2.5.5, AAA)**, run only at AAA and
  only with the display density: an error for each clickable or long
  clickable item visible to the user, with an area, under 44 dp wide or
  high, unrounded, saying "too narrow", "too short" or both, for
  example "40.2 by 40.2 dp, too narrow and too short; WCAG 2.5.5 needs
  44 by 44 dp". As for the other touch target checks, an item against
  the edge of a list that scrolls is not run, as the list may cut it
  off.
- **The reporting.** The line under "Show current visit" says the level
  and what it holds the checks to, before the findings: "Audit level
  AA: text contrast is held to WCAG 1.4.3, and targets to 2.5.8." or
  "Audit level AAA: text contrast is held to WCAG 1.4.6, and targets to
  44 by 44 dp (WCAG 2.5.5) as well as 2.5.8; 2.5.5's exceptions (an
  equivalent target, a target in text, one sized by Android, an
  essential presentation) are not judged." Since step 17, the line
  says which exceptions are judged from the tree instead.
- **Changing the level** works the text contrast findings out again
  from the same measurement, and runs the tree's checks again at the new
  level; if they are still running, they are run again when they end.
- **Run on the development PC** with test 16's recording (session
  20261112-154851-32c7b61a5552453bbf5febffcca978c7): at AA, 11 findings
  of the tree (10 errors, 1 not run), as before; at AAA, 12 (10 errors,
  2 not run). The one added is on visit 6: "Not run: Button described as
  "Help & FAQ Get Support", 345.4 by 23 dp, too short; WCAG 2.5.5 needs
  44 by 44 dp; not run, as it is against the edge of a list that
  scrolls, so may be cut off by it". No other target in the recording's
  checked snapshots is under 44 dp.
- **Checked before any PC test:** the solution builds with no warnings,
  and the tests pass, 2,381 of them, with the 10 that need Windows
  failing as before. The new tests cover a 40.2 dp button at AAA and at
  AA, a target 44 dp wide but 43.9 dp high, a 44 dp square passing, a
  target against a list's edge, no density, the run at AAA, and the old
  setting's name.
- **Limits.** 2.5.5's exceptions were not judged: a target with an
  equivalent of 44 dp, one in text, one sized by Android and one whose
  size is essential were all reported, and needed a person to set
  aside. Step 17 judges a target in text and an equivalent target.
  Section headings (2.4.10) and other AAA criteria are not checked.

## Step 8: images of text

### The owner's decision (2026-11-12)

"Since we have already identified issues the images of text in the app,
let's implement that first": the first of the candidates in
`android-checks-candidates.md` to be built is the probable image of
text, as measured there in the client's app. The separate finding for
text found by OCR in no item is not part of this step.

### What the standards ask (checked in cited documents)

- **1.4.5 Images of Text (AA):** "If the technologies being used can
  achieve the visual presentation, text is used to convey information
  rather than images of text", except where the image of text can be
  visually customized to the user's requirements, or a particular
  presentation is essential; logotypes are considered essential.
  WCAG2ICT applies it to software as written
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/#sc1-4-5-images-of-text)).
- **An image of text** is "text that has been rendered in a non-text
  form (e.g., an image) in order to achieve a particular visual
  effect", and does not include text that is part of a picture with
  significant other visual content
  ([Understanding SC 1.4.5](https://www.w3.org/WAI/WCAG22/Understanding/images-of-text.html)).
- **1.4.9 Images of Text (No Exception) (AAA):** images of text are only
  used for pure decoration or where a particular presentation is
  essential ([WCAG 2.2](https://www.w3.org/TR/WCAG22/)). WCAG2ICT does
  not cover it; it is applied here as written, at the AAA audit level
  only.

### As built

- **The rule** (`PhoneImagesOfTextRun`, in `Recorder.Session`). On each
  settled snapshot whose picture OCR read, every image in the app's
  window is looked at: an item visible to the user, with an area, whose
  class is `android.widget.ImageView` or a class drawn from it
  (`ImageButton` is one). Each line OCR read goes to the smallest image
  its centre is in, unless an item that is not an image holds it in its
  text or hint, as a `TextView` drawn over a picture would: that is the
  app's text. Words matched only to a description, as in the client's
  cards, show no text of the item's own, so count as inside the image.
  An image with at least 3 letters or digits read inside it
  (`MinimumCharacters`, an assumption, so that a shape read as a letter
  or two is not a finding) is a warning.
- **Why a warning.** The exceptions are not judged: a logotype, a
  picture with significant other visual content, such as a photograph
  or chart, and an image that can be customized are all reported, and
  need a person to set aside. Each finding says so.
- **The finding** is on the image, outlined at its bounds, and says what
  OCR read and how: for example "Warning: ImageView described as "Sleep
  Hygiene Steps to take for a good night's sleep.", OCR (Windows OCR,
  en-US) read 2 lines of text inside the image: "Sleep Hygiene", "Steps
  to take for a good night's sleep."; the words are 11.9 to 17.2 dp tall
  in OCR's box; its description holds them; a logotype, a picture with
  significant other visual content and an image that can be customized
  are not judged; approximate, from the video". Up to 3 lines are
  quoted, then "and N more". The heights are the OCR boxes', in dp with
  the display density, else in pixels; they are not the font's size.
  Whether the image's description holds the words is "holds them",
  "holds some of them", "holds none of them" or "it has no description",
  as spelt for matching. "From the phone's screenshot" or "approximate,
  from the video" says which picture OCR read.
- **One finding for each image in a visit**, with each settled snapshot
  it was seen in, as for the other checks; the same image on another
  visit is another finding.
- **At AAA** each image is also a warning to "Image of text (WCAG 1.4.9,
  AAA)", saying "1.4.9 allows an image of text only as pure decoration
  or where essential, as a logotype; neither is judged, nor a picture
  with significant other visual content". The 1.4.5 findings are the
  same at both levels.
- **In the Checks tab**, under each visit checked, after the non-text
  contrast rows: the bold row "Image of text (WCAG 1.4.5): 4 warnings,
  from 4 images read by OCR" and a row for each finding; or "no
  findings, from N images read by OCR"; or "no image in this visit"; or
  "not checked, as OCR read no picture of this visit". With OCR not used
  at all, there are no rows, and the line under "Show current visit"
  says it was not checked, as it needs OCR's words.
- **The line under "Show current visit"** adds, after non-text contrast,
  for example "Image of text (WCAG 1.4.5): 4 findings (4 warnings), from
  4 images read by OCR, 0 passed; the words OCR read inside an image
  that no item's text holds are a probable image of text, a warning, as
  the exceptions, such as a logotype, are not judged." The audit level's
  sentence now says images of text are held to 1.4.5 at AA, and to 1.4.9
  as well as 1.4.5 at AAA.
- **No new reading.** The check uses the OCR words and lines the
  contrast measurement already has; the snapshot's window is kept with
  its measurement so its images are known. Changing the audit level
  works the findings out again from the same measurement.

### Run on the development PC's recordings (measured 2026-11-12)

- **Test 16** (session 20261112-154851-32c7b61a5552453bbf5febffcca978c7,
  from video frames): 5 settled snapshots read by OCR, 4 images, all on
  visit 2 to Sleep Learning Library, 4 warnings, each seen once, at
  00:00:28.202: "Understanding Melatonin" (3 lines, 9.4 to 17.2 dp),
  "Sleep Hygiene" (2 lines, 11.9 to 17.2 dp), "Sleeping Environment" (3
  lines, 10.7 to 17.2 dp) and "Emotion, Health and Sleep" (2 lines, 11.5
  to 17.2 dp), each with "its description holds them". At AAA, 8
  warnings, the 4 to 1.4.9 after the 4 to 1.4.5. No other visit has an
  image.
- **Test 18** (session 20261112-171107-88cfc05214a34a3682222e7fb25d840d,
  from the phone's lossless screenshots): the same 4 warnings on visit 2,
  at 00:00:13.452, "from the phone's screenshot", with the same lines and
  heights.
- **Checked before any PC test:** the solution builds with no warnings,
  and the tests pass, 2,385 of them, with the 10 that need Windows
  failing as before. The new tests cover a described picture of a title
  and subtitle, a `TextView` over a picture, an icon read as two
  letters, a picture with no description, the merge across two
  snapshots from a frame and a screenshot, AAA, no OCR, a description
  holding some of the words, and words going to the smaller of two
  images.

### Limits

- The exceptions are not judged, as above.
- OCR misses stylised, small or low-contrast text, so an image of text
  can be missed; a picture drawn in a view that is not an `ImageView`
  or, since step 26, a web image (a custom view, or a Compose image
  left out of the tree) is not looked at. Text found by OCR in no item is a separate candidate, not
  built.
- A later AI-free confirmation is in `android-checks-candidates.md`:
  text in an image does not grow with Android's font size.

## Step 9: Label in Name

### The owner's decisions (2026-11-12)

"Next let's do Label in Name", the first of the easy wins in
`android-checks-candidates.md`. Three questions were put to the owner,
and answered:

1. **What a control's label is:** "All text inside it", the text of the
   control and of the items drawn inside it.
2. **A label read only by OCR:** "Error, saying OCR". Where the tree has
   no text for a control's label, the words OCR read inside it are its
   label, and a failure is an error like any other, saying it was read
   by OCR.
3. **Labels outside the control:** "labeledBy only". Text beside a
   control is not taken as its label, unless the control is labelled by
   it in the tree (Android's `labeledBy`).

### What the standards ask (checked in cited documents)

- **2.5.3 Label in Name (A):** "For user interface components with
  labels that include text or images of text, the name contains the
  text that is presented visually." WCAG2ICT applies it to software as
  written ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)).
- **Where the label is.** Conventionally it is the adjacent text, and
  for buttons and tabs it is the text "inside buttons and tabs or
  immediately below icons serving as buttons"; only the adjacent text
  should be treated as a label
  ([Understanding SC 2.5.3](https://www.w3.org/WAI/WCAG22/Understanding/label-in-name.html)).
- **What does not matter.** "Differences in capitalization and
  punctuation are not relevant when evaluating this criterion": "first
  name" passes for "First Name:", and "Next" for "Next…"
  ([Understanding SC 2.5.3](https://www.w3.org/WAI/WCAG22/Understanding/label-in-name.html)).
- **Symbols are not labels.** Text "used in a symbolic manner", such as
  "B" for Bold or ">" for Next, is not a visible label
  ([Understanding SC 2.5.3](https://www.w3.org/WAI/WCAG22/Understanding/label-in-name.html)).
- **Text in round brackets** "may be optionally considered a description
  and left out of the accessible name": "Name (required)" can accept
  "Name"
  ([Understanding SC 2.5.3](https://www.w3.org/WAI/WCAG22/Understanding/label-in-name.html)).
- **Failures listed:** the name not containing the visible label text
  (F96); the name containing the label's words "not in the same order";
  and the name containing the label with "other words are interspersed"
  ([Understanding SC 2.5.3](https://www.w3.org/WAI/WCAG22/Understanding/label-in-name.html)).
- **Best practice:** "A best practice is to have the text of the label
  at the start of the name"
  ([Understanding SC 2.5.3](https://www.w3.org/WAI/WCAG22/Understanding/label-in-name.html)).

### Measured first (2026-11-12)

On the recordings of tests 16 and 18, with a throwaway program and the
OCR the contrast check already reads (measured):

- **The client's app puts its text in content descriptions.** Its
  buttons, cards and tabs have content descriptions, and none of their
  items has tree text, so every control's label is known only from OCR.
  That is why a label read only by OCR is checked, and why each finding
  says how its label was read.
- **Test 16** (session 20261112-154851-32c7b61a5552453bbf5febffcca978c7,
  from video frames): 47 controls checked, all 47 passed, 3 with no
  label read: 11 on visit 1 to Welcome, 8 on visit 2 to Sleep Learning
  Library, 8 on visit 4 to Sleep Diary, 9 on visit 6 to Bob Dodd and 11
  on visit 7 to Welcome; visits 3 and 5 have no settled snapshot. The
  3 with no label read are a button described as "3" (the notification
  bell, whose orange badge "3" OCR did not read), on visits 1 and 7, and
  "Help & FAQ / Get Support", cut off at the bottom of the screen, on
  visit 6.
- **Test 18** (session 20261112-171107-88cfc05214a34a3682222e7fb25d840d,
  from the phone's screenshots): 27 controls checked, all 27 passed, 1
  with no label read: 11 on visit 1, 8 on visit 2 and 8 on visit 3.
- **So the app has no Label in Name finding** in these recordings. The
  bell button named "3" is a question of its name, for 1.1.1 and 4.1.2,
  not of 2.5.3.

### As built

- **The rule** (`PhoneLabelInNameRun`, in `Recorder.Session`). On each
  settled snapshot, every control in the app's window is looked at: an
  item visible to the user, with an area, that is clickable, long
  clickable, checkable or editable, and has a content description. The
  content description is its name. A control with no content
  description is not checked, as Android names it from its text.
- **Its label** is all the text inside it (decision 1), in reading
  order, top to bottom and left to right: the tree text of the control
  and of the visible items drawn inside it, and, where an item has no
  tree text, the words OCR read whose centre is inside the control and
  that no item's text holds (decision 2). The text of a control drawn
  inside another control, as a button inside a card, is that control's
  label, not the outer one's. An editable field's text is what was typed,
  so it is not its label. Each item the control is labelled by
  (`labeledBy`) gives a second label, its own text inside it
  (decision 3).
- **A label shorter than 2 letters or digits is not checked**
  (`MinimumCharacters`, an assumption: a single character is likely a
  symbol, such as "B" or ">"). A control with no label read is counted
  as "with no label read".
- **The match.** The label's words and the name's words are compared as
  spelt for matching, with case and punctuation ignored and
  apostrophes dropped (so "night's" and "night’s" are alike). The
  label's words must be in the name together and in order. A word read
  by OCR may differ by one letter (4 to 7 letters) or two (8 or more),
  as OCR misreads. Words run together on one side and apart on the
  other ("BobDodd" and "Bob Dodd") match. A label with text in round
  brackets is tried again without it.
- **Two discrete findings,** on the control, outlined at its bounds:
  - **"Label not in name (WCAG 2.5.3)",** an error, when the name does
    not contain a label, saying which words are missing, or "its words
    are all in the name, but not together in this order" when they are
    there out of order or with other words between them. For example
    "Error: Button described as "Find items", its name "Find items" does
    not contain the text inside it, "Search": "Search" is not in the
    name; the label is the app's text, from the tree".
  - **"Label not at the start of the name (WCAG 2.5.3, best practice)",**
    a warning, when the name contains the label but does not start with
    it.
- **Each finding says how its label was read:** "the label is the app's
  text, from the tree", or "the label was read by OCR (Windows OCR,
  en-US), as the tree has no text for it, from the phone's screenshot"
  (or "approximate, from the video"), or both, when some of it is each.
- **One finding for each control and check in a visit**, with each
  settled snapshot it was seen in, as for the other checks.
- **In the Checks tab**, under each visit checked, after the images of
  text rows: "Label in name (WCAG 2.5.3): no findings, from 8 controls
  checked."; or the bold row with its findings; or "no control with a
  label and a content description in this visit."
- **The line under "Show current visit"** adds, after images of text,
  for example "Label in name (WCAG 2.5.3): no findings, from 47 controls
  checked, 47 passed, 3 with no label read; the label is all the text
  inside a control, and the text of any item it is labelled by, from the
  tree's text and, where it has none, OCR's, against its content
  description; text beside a control is not taken as its label."
- **The same at both audit levels:** 2.5.3 is level A.
- **No new reading.** The check uses the snapshots' windows and the OCR
  lines the contrast measurement already has.
- **Checked before any PC test:** the solution builds with no warnings,
  and the tests pass, 2,401 of them, with the 10 that need Windows
  failing as before. The new tests cover a label from the tree that
  passes and one that fails, a label read by OCR not at the start, words
  out of order, a labelled-by item, text in round brackets, a symbol, an
  OCR misreading of one letter, a button inside a card, an editable
  field, an item inside a control with only a description, words found
  by OCR in no item, the merge across two snapshots, and the match's
  rules on their own.

### Limits

- Text beside a control is not its label unless the tree says so
  (decision 3), so a field labelled by text beside it, with no
  `labeledBy`, is not checked; an editable field's hint (its
  placeholder) is not taken as its label.
- The name is the content description only. A control named by its own
  text, with no content description, is not checked, as its name is its
  text.
- OCR misses stylised, small or low-contrast text, so a label read only
  by OCR can be missed or misread; a misread of more than the letters
  allowed is a false error, and each such finding says the label was read
  by OCR.

## Step 10: clickable but not focusable

### The owner's decision (2026-11-12)

"Clickable but not focusable" is item 2 of the easy wins in
`android-checks-candidates.md`. Measured first (below), the client's
app has no clickable item that is not focusable, and its tree marks
focusable what a screen reader can reach, so the tree cannot show which
items a keyboard reaches in it. Three other ways were put to the owner:
the recorder sending Tab key presses to the phone through adb, a Tab
walk by hand, or moving on to the checks of fields. The owner chose
"Tree-only check": build the check as proposed; it finds nothing in this
app, and the line under "Show current visit" says that a tree like this
app's cannot show it.

### What the standards and Android say (checked in cited documents)

- **2.1.1 Keyboard (A)** applies to software as written
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)).
- **Android's default.** `FOCUSABLE_AUTO` "Indicates that the view
  determines focusability automatically. This is the default value for
  focusable.", and "Setting this to FOCUSABLE_AUTO will cause the view to
  be focusable if it is clickable or long clickable"
  ([Android View reference](https://developer.android.com/reference/android/view/View#FOCUSABLE_AUTO)).
  So a clickable view that is not focusable has usually been made so by
  the app.
- **Flutter's tree.** Flutter's Android bridge sets each node's
  focusable from its own `isFocusable()`
  (`result.setFocusable(semanticsNode.isFocusable())`, in
  [Flutter's AccessibilityBridge](https://raw.githubusercontent.com/flutter/engine/main/shell/platform/android/io/flutter/view/AccessibilityBridge.java),
  now in `configureFocusable` of
  [Flutter's BaseRoleConfigurator](https://raw.githubusercontent.com/flutter/flutter/master/engine/src/flutter/shell/platform/android/io/flutter/view/BaseRoleConfigurator.java)).
  Unless a node says otherwise, that is true for any action other than
  scrolling, for its "Flags that make a node accessibility focusable"
  (such as a checked state), and for any label, value or hint
  ([Flutter's AccessibilityBridge](https://raw.githubusercontent.com/flutter/flutter/master/engine/src/flutter/shell/platform/android/io/flutter/view/AccessibilityBridge.java)).
  So in an app drawn with Flutter, focusable in the tree is about what a
  screen reader can reach, and every item with text is focusable.

### Measured first (2026-11-12)

With a throwaway program, on the recordings to hand (measured):

- **Test 16** (session 20261112-154851-32c7b61a5552453bbf5febffcca978c7):
  7 settled snapshots, 50 clickable items checked, 0 set aside, none
  not focusable; in `com.pss.novasleepcare` every item with text that
  is not a control is focusable.
- **Test 18** (session 20261112-171107-88cfc05214a34a3682222e7fb25d840d):
  28 clickable items checked, 0 set aside, none not focusable, and the
  same for its items with text.
- **Earlier recordings:** of Settings' "Safety and emergency" (session
  20261111-002648-06be83ce21ec47309bd4bc68b4ae1cb4), 36 clickable items,
  none not focusable; of Settings' "Display size & text" (session
  20261112-031508-262e4d97af4847dca17d45d0131b4814), 55, none not
  focusable; of the client's app and the Pixel's home screen (session
  20261112-005243-5528d67531dd4441a50b2c2985350c59), 156, 4 set aside
  inside a focusable clickable item, none not focusable. In Settings
  and the home screen, some items with text are not focusable, as in a
  view-based app; in the client's app every one is.
- **So the client's app is drawn with Flutter** (inference, from its
  tree matching Flutter's rules above, with no `ComposeView` in its
  windows: the only `ComposeView` items are `com.android.systemui`'s).
  This corrects step 1's inference that it is drawn with Jetpack
  Compose.

### As built

- **The rule** (`PhoneFocusChecks`, in `Recorder.Session`). On each
  settled snapshot, in the app's window, each item shown (visible to the
  user, with an area) that is enabled and clickable or long clickable is
  checked. One inside an item that is focusable and clickable (or long
  clickable), as a button in a card, is set aside, as that item takes
  the focus. A checked item that is not focusable is a warning, to
  "Clickable but not focusable (WCAG 2.1.1)", outlined at its bounds:
  for example "it is clickable but not focusable, and no item it is in
  is focusable and clickable, so a keyboard or switch user may not reach
  it; Android makes a clickable view focusable unless the app says not;
  a likely failure, until a keyboard test confirms it" ("long
  clickable" for an item only long clickable).
- **A Flutter-style tree is named.** The items with text or a content
  description that are not controls are counted, with how many of them
  are focusable. An app in which there is at least one and every one is
  focusable is named on the line under "Show current visit".
- **One finding for each item in a visit**, with each settled snapshot
  it was seen in, as for the other tree checks, listed under each visit
  in the Checks tab.
- **The line under "Show current visit"** adds, after "Checks of the
  tree: ...", for example "Clickable but not focusable (WCAG 2.1.1), one
  of them: no findings, from 50 clickable items checked, 0 set aside
  inside a focusable clickable item; each is a warning, until a keyboard
  test confirms it." and, for such an app, "In com.pss.novasleepcare,
  every item with text is focusable, as in an app drawn with Flutter,
  whose tree marks focusable what a screen reader can reach, so the tree
  does not show which items a keyboard reaches."
- **The same at both audit levels:** 2.1.1 is level A.
- **Checked before any PC test:** the solution builds with no warnings,
  and the tests pass, with the Windows-only browser tests failing as
  before. The new tests cover a clickable item not focusable, one
  focusable, one inside a focusable card, one only long clickable, one
  not enabled, one not shown, the counts of items with text in a
  view-based tree and in a Flutter-style one, and the counts across a
  recording.

### Limits

- The tree does not show what a keyboard reaches. A focusable item may
  still not take the keyboard's focus, and in a Flutter-style tree no
  clickable item is ever not focusable, so the check finds nothing
  there. A keyboard test, by hand or with Tab key presses sent through
  adb, would confirm it; that is not built.
- Focusable in touch mode is not considered, and an item not enabled is
  not checked.

## Step 11: fields and states

### The owner's decisions (2026-11-13)

The checks of fields and states are item 3 of the easy wins in
`android-checks-candidates.md`. All four were measured first (below),
then put to the owner:

- **A field labelled only by its hint fails.** "Why would hint alone
  pass? When you type in the text field the hint is no longer visible,
  and you fail WCAG 3.3.2" (the owner).
- **Text beside a field is not its label:** "labeledBy only", as for
  Label in Name. A field with only a hint is an error to 3.3.2, and the
  finding quotes the text just above it, not linked to it, as a possible
  label.
- **Invalid with no error text, and expandable with no state:** "Build
  both, tree-only".
- **A slider with no range information:** "Range info required", an
  error whatever its text or description says.
- **Each issue is discrete** (the owner, as for the other checks): no
  label and no name are two findings.

### What the standards and Android say (checked in cited documents)

- **3.3.2 Labels or Instructions (A):** "When labels or instructions are
  provided, they need to be persistently available to users"; a label's
  association is 1.3.1, a name is 4.1.2
  ([Understanding 3.3.2](https://www.w3.org/WAI/WCAG22/Understanding/labels-or-instructions.html)).
  Both apply to software as written
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)).
- **Google's advice on fields:** "To label an editable TextView or
  EditText, use android:hint to display a descriptive text label within
  the item when it's empty", and "If an app's user interface already
  provides a text label for the editable item, define android:labelFor
  on the labeling View"; a content description on an EditText "may
  interfere"
  ([Editable View labels](https://support.google.com/accessibility/android/answer/6378120?hl=en)).
  The owner's decision is stricter than the first: a hint is not shown
  once text is typed, so it is not a persistent label.
- **Android's reference:** `setContentInvalid` "Sets if the content of
  this node is invalid. For example, a date is not well-formed.", and
  `setError` "Sets the error text of this node." `ACTION_EXPAND` is the
  "Action to expand an expandable node." From API 36, `setExpandedState`
  "Sets the expanded state of the node", and `EXPANDED_STATE_UNDEFINED`
  (0) is the "Expanded state for a non-expandable element"; before API
  36, an expandable item said its state only by the action it offered.
  `RangeInfo` is the "Class with information if a node is a range."
  ([AccessibilityNodeInfo reference](https://developer.android.com/reference/android/view/accessibility/AccessibilityNodeInfo)).
- **Flutter's tree:** a text field's hint is its label and hint joined
  (`result.setHintText(node.getTextFieldHint())`), no item is linked to
  another as its label, and no item is given an error, an invalid state
  or range information; a slider is a `SeekBar` with its value in its
  description; expand and collapse are added only with an expanded
  state, from API 36
  ([Flutter's AccessibilityBridge](https://raw.githubusercontent.com/flutter/flutter/master/engine/src/flutter/shell/platform/android/io/flutter/view/AccessibilityBridge.java),
  [Flutter's BaseRoleConfigurator](https://raw.githubusercontent.com/flutter/flutter/master/engine/src/flutter/shell/platform/android/io/flutter/view/BaseRoleConfigurator.java)).
  So in an app drawn with Flutter no field has a label in the tree, the
  checks of invalid and expandable items find nothing, and every slider
  is a finding.

### Measured first (2026-11-13)

With a throwaway program, on the recordings to hand (measured):

- **Test 16** (session 20261112-154851-32c7b61a5552453bbf5febffcca978c7)
  and **test 18** (session 20261112-171107-88cfc05214a34a3682222e7fb25d840d):
  no field, no item marked invalid or with error text, no expand or
  collapse action, no slider; every item's expanded state is 0.
- **The client's app and the home screen** (session
  20261112-005243-5528d67531dd4441a50b2c2985350c59): one field, an
  `EditText` in `com.pss.novasleepcare`'s "Sleep Diary" sheet (visit 4),
  with no text or description, the hint "Fell asleep quickly, woke up
  once...", and no item linked to it as its label. Just above it, 39
  pixels apart, is an item described as "Notes (optional)".
- **Settings' "Display size & text"** (session
  20261112-031508-262e4d97af4847dca17d45d0131b4814): the "Display size"
  and "Font size" sliders are `SeekBar`s with range information.
- **Settings' "Safety and emergency"** (session
  20261111-002648-06be83ce21ec47309bd4bc68b4ae1cb4): none of the four.

### As built

- **The rules** (`PhoneFieldChecks`, in `Recorder.Session`). On each
  settled snapshot, in the app's window, each item shown (visible to the
  user, with an area) is checked:
  - **"Field with no label (WCAG 3.3.2)"**, an error: an editable item
    with no item linked to it as its label (`labeledBy`) that is shown,
    or one outside the window. With a hint, for example "its only label
    is its hint, "Fell asleep quickly, woke up once...", which is not
    shown once text is typed, and no item is linked to it as its label
    (labeledBy)"; with none, "it has no label: no hint, and no item
    linked to it as its label (labeledBy)". When an item with text or a
    description, not a control, ends just above it (across from it, and
    no further above than the field is tall, as older recordings have no
    density), the finding adds, for example, "the text just above it,
    "Notes (optional)", is not linked to it as its label".
  - **"Field with no name (WCAG 4.1.2)"**, an error: such a field with
    no hint and no content description as well, "so a screen reader can
    say only what is typed in it".
  - **"Marked invalid with no error text (WCAG 3.3.1)"**, a warning: an
    item marked invalid with no error text, as the error may be shown as
    text not linked to it.
  - **"Expandable with no expanded state (WCAG 4.1.2)"**, a warning: an
    item with the expand or collapse action whose expanded state is 0,
    undefined, with no state description. Where its state was not
    recorded (before Android 16), it is counted, not checked.
  - **"Slider with no range information (WCAG 4.1.2)"**, an error: a
    `SeekBar` or `RatingBar`, or an item with the set progress action,
    with no range information; its text or description is quoted as
    where its value may be, "but not as a range".
- **One finding for each item in a visit**, with each settled snapshot
  it was seen in, listed under each visit in the Checks tab after
  "Clickable but not focusable".
- **The line under "Show current visit"** adds, after the clickable
  sentence, for example "Fields and states, five of them: 1 finding (1
  error), from 1 field, 0 marked invalid, 0 expandable items and 0
  sliders checked; a field's label is only an item linked to it as its
  label (labeledBy), as a hint is not shown once text is typed.", and
  says how many expandable items' state was not recorded, when any was
  not.
- **The same at both audit levels:** 3.3.1, 3.3.2 and 4.1.2 are level A.
- **On the recordings to hand** (measured, with the throwaway program):
  the client's app and home screen give one finding, the Sleep Diary
  field's, in visit 4, quoting "Notes (optional)", at 00:00:41.101 on
  the PC (test 41): the picture of its snapshot, the last video frame
  before the window was next listed. The throwaway program's copy of the
  recording has no phone video, so gave the snapshot's own time,
  00:00:23.368;
  "Display size & text" gives 12 sliders checked and no findings; tests
  16 and 18 give none.
- **Checked before any PC test:** the solution builds with no warnings,
  and the tests pass, with the Windows-only browser tests failing as
  before. The new tests cover a field with a hint only and the text
  above it quoted, one labelled, one not shown, one with nothing (two
  findings) and text too far above, an invalid item with and without
  error text, expandable items with an undefined state, a defined
  state, an unrecorded state and a state description, and sliders with
  and without range information.

### Limits

- A label linked only by the layout, as Material's floating label of a
  `TextInputLayout` may be, cannot be told from the tree; such a field
  is a finding until it is looked at. In an app drawn with Flutter no
  field is linked to a label, so every field is a finding.
- The error text may be shown on screen, not linked to the item; the
  check does not read it, so it is a warning.
- An item that expands with no expand or collapse action, only a click,
  is not found.
- A slider drawn as a plain view with no set progress action is not
  found.

## Step 12: status messages

### The owner's decisions (2026-11-13)

4.1.3 is the first check in "Medium, from events and touches, by rules
that may mislead" in `android-checks-candidates.md`. It was measured
first (below), then put to the owner with three rules for what counts
as a status message:

- **The rule:** option 1, text that is shown and goes away by itself,
  with no touch between. "Option 1, but finding no live regions at all
  is a significant find. Not necessarily an error, but a warning to go
  look for any system messages that should be using one" (the owner).
- **The level of a brief message not exposed:** "Error".
- **Each issue is discrete** (the owner, as for the other checks): a
  brief message not exposed and an app with no live region are two
  checks.

### What the standards and Android say (checked in cited documents)

- **4.1.3 Status Messages (AA):** "status messages can be
  programmatically determined through role or properties such that they
  can be presented to the user by assistive technologies without
  receiving focus". A status message is a "change in content that is not
  a change of context, and that provides information to the user on the
  success or results of an action, on the waiting state of an
  application, on the progress of a process, or on the existence of
  errors". Text going away can be one too: "In situations where status
  text is entirely removed, its absence may itself convey information
  about the status", and "For a sighted user, when this text disappears,
  it is normally an indication that the state is now available". A
  dialog that takes focus "is defined as a change of context and does
  not meet the definition of a status message"
  ([Understanding 4.1.3](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html)).
  For software, there is "still a user need to have status messages be
  programmatically exposed so that they can be presented to the user by
  assistive technologies without receiving focus"
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)).
- **Android's reference:** a live region tells "accessibility services
  whether they should automatically notify the user about changes to
  the view's content description or text, or to the content
  descriptions or text of the view's children"; polite is to "announce
  changes to this view's content to the user", assertive to announce
  them "immediately". `announceForAccessibility` is a "Convenience
  method for sending a `TYPE_ANNOUNCEMENT` event", deprecated in API
  level 36: "Use `sendAccessibilityEvent(int)` with `TYPE_ANNOUNCEMENT`
  instead"
  ([View reference](https://developer.android.com/reference/android/view/View#announceForAccessibility(java.lang.CharSequence))).
- **Flutter's tree:** an item flagged as a live region is given the
  polite live region (`result.setLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE)`),
  and an announcement from the app is sent with
  `announceForAccessibility`
  ([Flutter's BaseRoleConfigurator](https://raw.githubusercontent.com/flutter/flutter/master/engine/src/flutter/shell/platform/android/io/flutter/view/BaseRoleConfigurator.java),
  [Flutter's AccessibilityBridge](https://raw.githubusercontent.com/flutter/flutter/master/engine/src/flutter/shell/platform/android/io/flutter/view/AccessibilityBridge.java)).
  So the check reads an app drawn with Flutter as it reads any other.

### Measured first (2026-11-13)

With a throwaway program, on the recordings to hand (measured):

- **No item in a live region, no announcement and no toast** in any of
  them.
- **Test 16** (session 20261112-154851-32c7b61a5552453bbf5febffcca978c7):
  three brief messages in `com.pss.novasleepcare`, each the loading
  item described as "Loading your personal sleep learning space.", with
  "☀️🌙", gone by themselves 0.41, 0.28 and 0.35 seconds after they
  were shown, in visits 2, 3 and 5.
- **Test 18** (session 20261112-171107-88cfc05214a34a3682222e7fb25d840d):
  one, in visit 2, gone 2.98 seconds after it was shown.
- **The client's app and the home screen** (session
  20261112-005243-5528d67531dd4441a50b2c2985350c59): one, in visit 2.
- **Settings' "Display size & text" and "Safety and emergency"**
  (sessions 20261112-031508-262e4d97af4847dca17d45d0131b4814 and
  20261111-002648-06be83ce21ec47309bd4bc68b4ae1cb4): none.

### As built

- **What it reads.** The playback index, version 30 since this step,
  keeps every `TYPE_ANNOUNCEMENT` event, and every
  `TYPE_NOTIFICATION_STATE_CHANGED` event, a toast when its class names
  one, with the touches and every taking of the tree, not only the
  settled snapshots (`PhoneStatusChecks`, in `Recorder.Session`).
- **"Status message not announced (WCAG 4.1.3)"**, an error. In each
  window, every taking of its items is followed. Text shown that is not
  a control (visible, with an area, not clickable, checkable or
  editable) and was not in the window's last taking, shown or not, is
  new; the window's first taking counts as new. New text gone in a later
  taking within 5 seconds, with no touch between, is a brief message;
  texts shown and gone together are one message. It is exposed when it,
  or an item it is inside, is in a live region, when it is inside a
  titled pane that was not in the last taking, or when the app made an
  announcement from 1 second before it was shown until it went. A brief
  message not exposed is a finding in the visit it was shown in, for
  example "View described as "Loading your personal sleep learning
  space.", it was shown with "☀️🌙" and they went away by themselves
  0.41 seconds later, with no touch between, as a status message does;
  it is not in a live region, no announcement was made and no titled
  pane appeared with it, so a screen reader is not told of it". It is
  shown at the first picture of the phone while it was shown. With no
  touches recorded, this check does not run, as text going away by
  itself cannot be told from text a touch took away.
- **"No live region in the app (WCAG 4.1.3)"**, a warning, once for
  each app: none of its items was in a live region in the whole
  recording, and it made no announcement and showed no toast. It is
  listed under the app's row, not under a visit, as it is about all its
  visits, for example "in its 7 visits in this recording, no item of the
  app was in a live region, and it made no announcement and showed no
  toast; look for status messages, such as saved, sent, loading,
  progress or error messages, that should be in a live region".
- **Every snapshot is read,** not only settled ones, as a status message
  is brief. A visit with no settled snapshot says "Not checked, as no
  snapshot of this visit settled, except for status messages, which are
  looked for in every snapshot.", with its status findings below.
- **The line under "Show current visit"** adds, for example, "Status
  messages (WCAG 4.1.3), two checks: 4 findings (3 errors, 1 warning); 3
  brief messages (text shown and gone within 5 seconds, with no touch
  between), 0 of them in a live region, announced or in a titled pane
  that appeared with them; 0 items in a live region, 0 announcements
  and 0 toasts in the recording."
- **The same at both audit levels:** 4.1.3 is level AA, so it runs at
  both.
- **On the recordings to hand** (measured, with the throwaway program):
  the brief messages above are each a finding; each app gets the
  warning, as none has a live region: `com.pss.novasleepcare` in tests
  16 and 18 and in the client's recording, where the home screen's
  launcher, `com.google.android.apps.nexuslauncher`, gets one too, and
  Settings in both Settings recordings. The check takes under a second.
- **Checked before any PC test:** the solution builds with no warnings,
  and the tests pass, with the Windows-only browser tests failing as
  before. The new tests cover a brief text not exposed (the error and
  the app's warning), one in a live region, one announced, one taken
  away by a touch, one already in the window, hidden, and a recording
  with no touches.

### Limits

- Content that moves on by itself, such as a carousel, is text shown
  and gone with no touch, so may be a finding that is not a status
  message.
- A message shown for longer than 5 seconds, or one that stays, is not
  found; nor is a message whose text changes in place, such as a count.
- A message taken away by a touch elsewhere in the window is not found.
- The home screen and other apps passed through get the app warning
  too; it is a warning to look, not a failure.
- The recordings to hand have no saved, sent or error message or
  snackbar; only loading text was found.

## Step 13: timing

### The owner's decisions (2026-11-13)

2.2.1 and 2.2.2 are the second item in "Medium, from events and touches,
by rules that may mislead" in `android-checks-candidates.md`. The
owner asked for them after step 12, as the loading text that step 12
found also went away on what may be a timer. They were measured first
(below), then put to the owner:

- **When text going away is a 2.2.1 finding:** "Only if gone alone, but
  in cases like the loading text on the site, we should provide a
  warning that the text did disappear without warning and we can't
  prove it wasn't a timer" (the owner). So text gone with nothing new
  shown is one check, and text gone as new content appeared is another,
  always a warning.
- **The level of text gone alone:** "Setting decides". The owner's
  reason: "2 minutes is simply too long. We are testing from a user
  screen recording, we can't expect them to notice a message and then
  pause for 2 minutes and see what happens." So the check uses the
  phone's "Time to take action" setting as recorded, not a fixed wait.
- **The level of 2.2.2's findings:** "Warning".
- **Each issue is discrete** (the owner, as for the other checks): four
  checks.

### What the standards and Android say (checked in cited documents)

- **2.2.1 Timing Adjustable (A):** "Any process that happens without
  user initiation after a set time or on a periodic basis is a time
  limit." A time limit can meet it by letting the user adjust it "over a
  wide range that is at least ten times the length of the default
  setting". Its example of a message that goes away: "the message
  disappears after 5 seconds"; when "Users are able to identify the
  arrival of email through other means" it passes, but "If the user has
  no other means" of finding it, "each message would need to meet this
  success criterion"
  ([Understanding 2.2.1](https://www.w3.org/WAI/WCAG22/Understanding/timing-adjustable.html)).
- **2.2.2 Pause, Stop, Hide (A):** for moving, blinking or scrolling
  content that starts automatically, lasts more than five seconds and is
  shown in parallel with other content, and for auto-updating content
  that starts automatically and is shown in parallel with other content,
  "there is a mechanism for the user to pause, stop, or hide it", unless
  it is essential. "Auto-updating" is content that "updates or
  disappears based on a preset time interval", and there is "no five
  second exception for auto-updating"; a loading animation is not in
  parallel with other content, and an animation shown while loading
  may be essential
  ([Understanding 2.2.2](https://www.w3.org/WAI/WCAG22/Understanding/pause-stop-hide.html)).
- **WCAG2ICT:** both apply to software as written, and adds, as a note
  to 2.2.2: "Any content, even if just decorative, that is updated
  automatically, blinks, or moves may create an accessibility barrier"
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)).
- **Android's "Time to take action":** "You can choose how long to show
  messages that ask you to take action, but are visible only
  temporarily", with Default, 10 seconds, 30 seconds, 1 minute and 2
  minutes; "Not all apps support this setting"
  ([Google's Android Accessibility Help](https://support.google.com/accessibility/android/answer/9426889?hl=en)).
  An app asks for it with `getRecommendedTimeoutMillis`: "Controls
  should remain on the screen for at least this long to give users time
  to react"
  ([AccessibilityManager reference](https://developer.android.com/reference/kotlin/android/view/accessibility/AccessibilityManager?authuser=7)).
  The system's own components and Material 3's honour it; a custom
  component has to ask
  ([Inclusive Android Apps #4](https://buttondown.com/inclusive-android-apps/archive/inclusive-android-apps-4-the-problem-of-time-based-actions/)).
  The recorder already records it, as Settings.Secure's
  `accessibility_non_interactive_ui_timeout_ms` ("Timeout for
  messages") and `accessibility_interactive_ui_timeout_ms` ("Timeout for
  controls").
- **Toasts:** Android keeps a toast up for the setting itself:
  `delay = mAccessibilityManager.getRecommendedTimeoutMillis(delay, AccessibilityManager.FLAG_CONTENT_TEXT);`
  ([AOSP's NotificationManagerService](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/services/core/java/com/android/server/notification/NotificationManagerService.java)).
  So toasts are not checked here.

### Measured first (2026-11-13)

With a throwaway program, on the recordings to hand (measured):

- **"Time to take action"** was Default (not set) at the start and the
  stop of tests 16 and 18 and of the client's recording.
- **The loading text:** in every case (three in test 16, one in test
  18, one in the client's recording) it was shown 0.03 to 0.04 seconds
  after the finger lifted from the tab bar, while the tab slid across,
  and went as the slide finished, with new content shown within 0.02
  seconds of it going. So each went as new content appeared.
- **Text changing in place with no touch:** only the bell button's text
  in test 16, "Notifications" to "3", 0.47 seconds after a tap, so the
  tap's.
- **Moving content:** in test 16, the only recording to hand with the
  phone's video, no stretch of the video kept changing for more than 5
  seconds with no touch; the longest was 2.47 seconds.

### As built

- **What it reads.** The same pass over every taking of each window as
  step 12 (`PhoneStatusChecks`, in `Recorder.Session`), with the
  phone's settings; and the phone's video frames
  (`PhoneMotionChecks`). It needs touches recorded, as step 12 does.
- **The time looked for.** "Time to take action" for messages
  (`accessibility_non_interactive_ui_timeout_ms`, which Android uses for
  a message of text with no controls) when the text was shown. When it
  is Default (not set, or 0) or not recorded, texts gone within 5
  seconds are looked at, as in step 12; when it is set, texts gone
  sooner than it are. Using the setting's own length when set is this
  step's reading of "Setting decides".
- **Text gone.** Every text shown and gone within that time with no
  touch between, as step 12 finds them, but whether it was exposed or
  not; texts shown and gone together are one. It went alone when no
  text that was not in the window's last taking was shown from half a
  second before it went to half a second after (text shown with it does
  not count).
- **"Text gone by itself (WCAG 2.2.1)"**, for text gone alone: a warning
  when Time to take action was Default or not recorded, as the app may
  keep it up longer when the setting is set, for example "it was shown
  and went away by itself 0.50 seconds later, with no touch between and
  nothing new shown as it went, as a message on a timer does; the
  phone's Time to take action was Default, so whether the app keeps it
  up longer for a user who needs more time is not known; record with
  Time to take action set, such as 10 seconds, to tell"; an error when
  it was set, as the app did not keep the text up for the time chosen,
  ending "though the phone's Time to take action was 10 seconds, so the
  app did not keep it up for the time the user chose; unless what it
  said can be found another way, the user cannot have more time to read
  it".
- **"Text gone as new content appeared (WCAG 2.2.1)"**, a warning, for
  example "View described as "Loading your personal sleep learning
  space.", it was shown with "☀️🌙" and they went away 0.41 seconds
  later, with no touch between, as new content appeared, as when
  loading finishes; it went without warning, and the recording cannot
  show that it was not on a timer".
- **"Text updating by itself (WCAG 2.2.2)"**, a warning. An item's text
  changed in place when the item at the same place in the window's tree,
  of the same class, has other text than in the window's last taking,
  with no touch from a second before that taking until this one. An
  item whose text changed so twice or more in a visit, at least a second
  apart, is a finding, with each change as a time, for example "its text
  changed 2 times with no touch, here from "10:00" to "10:01", as
  auto-updating content does; look for a way to pause, stop or hide it,
  or to set how often it updates, unless the updating is essential". Its
  old text going is then not also a text gone, as it was replaced.
- **"Moving by itself, from the video (WCAG 2.2.2)"**, a warning. The
  recorder writes a frame of the phone's video only when the picture
  changed, so a run of frames each less than half a second after the
  last is a picture that kept changing. A run lasting more than 5
  seconds, with no touch in it and none in the second before it, is
  looked at: 8 pairs of frames spread through it are compared, every
  fourth pixel each way, a pixel changed when a colour differs by more
  than 24. Changes outside the app's window, or under a window above it
  such as the status bar, are left out; with fewer than 16 pixels
  changed, it is not movement. The finding is on the smallest item
  holding the box that changed, outlined with the box, for example "from
  the video, so approximate: the picture in the outlined box kept
  changing for 6.5 seconds with no touch, as moving, scrolling or
  auto-updating content does; if it is more than a loading indicator
  shown alone, look for a way to pause, stop or hide it, unless the
  movement is essential". It does not run without the phone's video.
- **Where findings go:** each under the visit the text was shown in, or
  the change or movement started in, as for the other checks.
- **The line under "Show current visit"** adds, for example, "Timing
  (WCAG 2.2.1 and 2.2.2), four checks: 3 findings (3 warnings); Time to
  take action was Default, so texts gone within 5 seconds were looked
  at: 3 texts with no touch between, 0 of them gone with nothing new
  shown as they went; 0 items whose text changed in place with no
  touch; 0 parts of the phone's video that kept changing for more than 5
  seconds with no touch (from the video, so approximate)."
- **The same at both audit levels:** 2.2.1 and 2.2.2 are level A.
- **On the recordings to hand** (measured, with the throwaway program):
  each loading text is a "Text gone as new content appeared" warning,
  in visits 2, 3 and 5 of test 16, visit 2 of test 18 and visit 2 of the
  client's recording; no text gone alone, no text updating by itself,
  and, in test 16, no moving content. Neither Settings recording has a
  finding. The checks take under a second.
- **Checked before any PC test:** the solution builds with no warnings,
  and the tests pass, with the Windows-only browser tests failing as
  before. The new tests cover text gone alone with the setting not
  recorded, Default, 0, 10 seconds and 1 minute; text gone as new
  content appeared, with the setting Default and 10 seconds; text taken
  away by a touch and a recording with no touches; text updating twice
  with no touch, and with a touch before the second change; and the
  video changing for 6.5 seconds in the page, in the status bar only,
  and with a touch partway.

### Limits

- Text gone alone is a warning, not an error, at Default: the recording
  cannot tell whether the app keeps the text up longer when the setting
  is set. Recording with it set tells.
- Text shown for longer than the time looked for is not found, nor is a
  time-out of a whole screen, such as a session ending; that waits for
  the deferred APK and timer work.
- "Gone alone" is judged from text only: new content that is a picture,
  with no text, is not seen, so text gone as a picture appeared is
  "gone by itself".
- The place of an item is its position in the tree, so an item that
  moves in the tree as others are added can look like text changing in
  place; two changes a second apart are needed to lower this. A clock on
  the screen, such as the home screen's, is text updating by itself.
- Text that updates every few seconds is also, each time, a brief
  message to step 12, which is left as it was.
- Moving content is from the video, so approximate: a run is broken by
  any touch, so content moving while the user touches the screen is not
  found, and the box is everything that changed in the pairs compared.
- The recordings to hand have no carousel, ticker, counter or
  animation that runs by itself, so the text updating and moving
  content checks were only seen to find nothing on them.

## Step 14: changes of context

### The owner's decisions (2026-11-13)

3.2.1 and 3.2.2 are the third item in "Medium, from events and touches,
by rules that may mislead" in `android-checks-candidates.md`. The owner
asked for them after step 13. They were measured first (below), then
put to the owner:

- **Which focus 3.2.1 looks at:** "Keyboard and TalkBack". So there are
  two checks: the input focus moved by a key or by a tap on a text
  field, and TalkBack's focus moved by a gesture with no double tap.
- **The level of 3.2.1's findings:** "Error".
- **The level of 3.2.2's findings:** "Warning", as a change is allowed
  when the user was told of it before, which the recording cannot show.
- **Each issue is discrete** (the owner, as for the other checks): three
  checks.

### What the standards say (checked in cited documents)

- **3.2.1 On Focus (A):** "When any user interface component receives
  focus, it does not initiate a change of context"
  ([Understanding 3.2.1](https://www.w3.org/WAI/WCAG22/Understanding/on-focus.html)).
- **3.2.2 On Input (A):** "Changing the setting of any user interface
  component does not automatically cause a change of context unless the
  user has been advised of the behavior before using the component".
  "Checking a checkbox, entering text into a text field, or changing the
  selected option in a list control changes its setting, but activating
  a link or a button does not"
  ([Understanding 3.2.2](https://www.w3.org/WAI/WCAG22/Understanding/on-input.html)).
- **A change of context:** "Opening a new window, moving focus to a
  different component, going to a new page ... or significantly
  re-arranging the content of a page are examples of changes of
  context"; "Changes in content, such as an expanding outline, dynamic
  menu, or a tab control do not necessarily change the context, unless
  they also change one of the above (e.g., focus)"
  ([Understanding 3.2.1](https://www.w3.org/WAI/WCAG22/Understanding/on-focus.html)).
- **WCAG2ICT:** both apply to software as written
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)).
- **Failures named by W3C:** F55 (focus removed when an item receives
  it), F36 (a form submitted when its last field is filled
  in) and F37 (a new window opened when a radio button, check box or
  select list is changed); techniques G13 (describing what will happen
  before the change) and G80 (a submit button)
  ([Understanding 3.2.1](https://www.w3.org/WAI/WCAG22/Understanding/on-focus.html),
  [Understanding 3.2.2](https://www.w3.org/WAI/WCAG22/Understanding/on-input.html)).

### Measured first (2026-11-13)

With a throwaway program, on the recordings to hand (measured):

- **Test 16, test 18 and the client's recording (TalkBack off):** no
  keys, no typing, no taps on a field, a check box, a switch or a
  slider. Every tap that changed the screen was on a button, tab or row,
  which is activation, not a change of setting.
- **The Display size recording:** two clicks, on the plus and minus
  buttons, each followed by a new visit; activation, so not looked at.
- **The Settings recording with TalkBack on (Safety and emergency):**
  four moves of TalkBack's focus by the user's swipes, none followed by
  a change of context; the focus TalkBack put on "Navigate up" 0.49
  seconds after the Safety and emergency screen opened is TalkBack
  placing its focus on the new screen, not the user moving it.

So no recording to hand has a finding, as expected; the checks were
seen to find nothing on them.

**Test 48's recording (2026-11-13,
`20261113-200327-fdc058f1017a4496b2158acb591c6db4`), measured:** the
tap on the "Select to Speak shortcut" switch lifted at 8.983 seconds,
and the switch's click event ("ON") came 0.02 seconds later; Select to
Speak was already allowed, so no permission message appeared, but a
"Quick Settings shortcut" dialog (`androidx.appcompat.app.AlertDialog`,
with "Got it") took the focus, listed at 9.209 seconds, 0.23 seconds
after the tap. While it was up, Android listed the dialog's window,
untitled, and not the Settings window under it, so the dialog was not a
state of the visit, and the first build found no change of context. As
built since, a window of an app's that appears and takes the focus is a
change of context whether or not the window under it is listed, and
the recording gives one warning. On the PC (test 50) it is shown at
00:00:09.203, the phone's screenshot showing the dialog, as a finding is
shown at the first picture between the action and the change.

### As built

- **What it reads.** The phone's accessibility events (`PhoneInputTimeline`,
  in `Recorder.Session`): clicks, long clicks, selections, the input
  focus, text changes, TalkBack's focus and the start of a touch that
  TalkBack handled; and the phone's keys and touches. The playback index
  keeps these events (its version is now 31, so older recordings are
  indexed again when opened). It needs touches recorded: with none, a
  tap that caused a change cannot be told from none, and the checks do
  not run.
- **What the user did.** Each touch is matched to the item under it: the
  topmost window at that place, by layer, and in it the smallest
  enabled, visible item that is clickable, checkable, editable or a
  slider. Touches on the keyboard are left out; a touch on the system's
  bars, or one TalkBack handled, is activation. A tap on an editable
  item gives it the focus; a tap on a checkable item, or on a row that
  holds one switch or check box that is not clickable itself (as in
  Android's Settings), changes its setting, as does moving a slider;
  any other touch is activation. The click event a touch causes, up to
  half a second after the finger lifted, is the same action, and makes
  it a change of setting when the item clicked is checkable. Tab and
  the arrow keys move the focus; the volume, power and modifier keys are
  left out; other keys are activation. Text typed in an app's field, a
  choice in a spinner or a new value in a seek bar or rating bar, and a
  click on a checkable item with no touch (from a keyboard), are changes
  of setting. TalkBack's focus moved by the user is its focus moving up
  to a second after a gesture or key ended, unless it moved up to a
  second after a change of context, which is TalkBack placing it.
- **A change of context.** A new visit to a different screen, or another
  app coming to the front; a window that took the focus or became active
  appearing over the app, such as a dialog, or any window of an app's,
  other than a screen's own window, that appears and takes the focus
  (as Settings' dialogs do, listed without the window under them); a
  pane appearing; or the
  input focus moving to an item other than the one a key or tap on a
  field gave the focus to (up to half a second after it).
- **A finding** is a focus move, TalkBack focus move or change of
  setting followed within 1.5 seconds by a change of context in the
  same visit, with no other action between. It is on the item acted
  on, found by its bounds and class, or the window's first item when
  that is not found, under the visit it happened in.
- **"Change of context on focus (WCAG 3.2.1)"**, an error, for example
  "a tap on the field gave it the focus, and 0.45 seconds later, with
  nothing activated, the window 'Allow?' appeared, a change of context
  on focus".
- **"Change of context on TalkBack focus (WCAG 3.2.1)"**, an error, for
  example "TalkBack's focus moved to it, and 0.45 seconds later, with no
  double tap, touch or key, the window 'Allow?' appeared, a change of
  context on a screen reader's focus".
- **"Change of context on input (WCAG 3.2.2)"**, a warning, for example
  "a tap changed its setting (it was not checked), and 0.45 seconds
  later, with no further touch or key, the window 'Allow?' appeared, a
  change of context on input, which is allowed only when the user was
  told of it before using the item; look for such advice".
- **The line under "Show current visit"** adds, for example, "Changes
  of context (WCAG 3.2.1 and 3.2.2), three checks: no findings; 0 focus
  moves by a key or a tap on a field, 0 TalkBack focus moves after a
  gesture or key, and 0 setting changes (typing, checking, choosing or
  moving a slider); 0 changes of context (a new screen or app, a window
  that took the focus, a pane, or the focus moving by itself) within 1.5
  seconds of one, with nothing touched, pressed or clicked between."
- **The same at both audit levels:** 3.2.1 and 3.2.2 are level A.
- **On the recordings to hand** (measured, with the throwaway program):
  no findings on any; the Settings recording with TalkBack on has 4
  TalkBack focus moves, and the others none of the three kinds.
- **Checked before any PC test:** the solution builds with no warnings,
  and the tests pass, with the Windows-only browser tests failing as
  before. The new tests cover a switch tapped, with and without its
  click event, a Settings row holding a switch tapped, and text typed,
  each followed by a dialog (and the switch by a new screen, and by an
  untitled dialog listed without the window under it); a tap on a
  field, the Tab key and TalkBack's focus moved by a gesture, each
  followed by a dialog; and, with no finding, a button tapped, with and
  without its click event, the volume key, a switch tapped 1.8 seconds
  before, another touch between, TalkBack's double tap between, a window
  that did not take the focus, and a recording with no touches.

### Limits

- A change is taken as the action's only by time: something the app did
  by itself within 1.5 seconds of an action is counted as the action's.
- Only what the tree and events show is a change of context: a
  significant re-arranging of content in the same window, with no new
  screen, window or pane, is not found.
- A window closing, such as a dialog going away, is not counted.
- Apps drawn with Flutter, such as the client's, may not send focus,
  click or text events, so their changes of setting are found from taps
  only; typing in them may not be seen.
- With a hardware keyboard, Space or Enter on a check box is seen only
  through its click event.
- A setting changed by choosing from a list is seen only when the list
  sends a selection event from a spinner, seek bar, rating bar or
  slider class.
- 3.2.2's exception, advice given before the item is used, is not looked
  for, so its findings are warnings.
- Of the recordings to hand, only test 48's has a finding, so the
  checks were seen finding something only there and in the tests.

## Step 15: orientation

### The owner's decisions (2026-11-13)

1.3.4 is in "Easy wins, from the tree already recorded" in
`android-checks-candidates.md`, needing a step in the test procedure.
The owner asked for it after step 14, "with a rotation step". The
recordings were looked at first (below), then the choices were put to
the owner:

- **What shows that the phone itself was turned:** "Companion's own
  sensor". A new companion app (protocol 7) reads the accelerometer
  while the recorder is connected. A feasibility test on the phone comes
  first, as Android may stop sensors for apps in the background.
- **What is reported:** "Warning, turn only": a warning, at both audit
  levels, when the phone was held turned with Auto-rotate on and the
  screen did not turn with it, since the recording cannot tell whether
  an orientation is essential.
- **How long the phone must be held turned:** "Setting decides": a
  setting in the Checks tab, "Phone turned for:", from 1 to 5 seconds,
  2 by default.

### What the standards and Android say (checked in cited documents)

- **1.3.4 Orientation (AA):** "Content does not restrict its view and
  operation to a single display orientation, such as portrait or
  landscape, unless a specific display orientation is essential".
  Examples where it may be essential include a bank cheque and a piano
  app. Its failures are F97, "locking the orientation to landscape or
  portrait view", and F100, "showing a message asking to reorient
  device"
  ([Understanding 1.3.4](https://www.w3.org/WAI/WCAG22/Understanding/orientation.html)).
- **WCAG2ICT:** applies it to software except software never shown on
  hardware that is turned in typical use
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)); a phone is turned in
  typical use.
- **Android 16:** for apps targeting it, orientation restrictions are
  ignored only on displays with a smallest width of at least 600 dp
  ([Android 16 behavior changes](https://developer.android.com/about/versions/16/behavior-changes-16)).
  The Pixel's screen, 1080 by 2404 pixels, is under that, so an app's
  restriction still holds on it.
- **Auto-rotate:** with it on, the window manager turns the display by
  the sensor unless the app in front asks for a fixed orientation
  (AOSP
  [DisplayRotation.java](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/services/core/java/com/android/server/wm/DisplayRotation.java)),
  so the display's rotation alone cannot show that the phone was turned:
  an app locked to portrait leaves it at 0. The setting is
  `Settings.System.ACCELEROMETER_ROTATION`, `system/accelerometer_rotation`.
- **The sensor while in the background:** an accessibility service is
  bound with `BIND_FOREGROUND_SERVICE_WHILE_AWAKE`
  ([AccessibilityServiceConnection.java](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/services/accessibility/java/com/android/server/accessibility/AccessibilityServiceConnection.java)),
  and the sensor service stops sensors only for apps it holds idle
  ([SensorService.cpp](https://android.googlesource.com/platform/frameworks/native/+/refs/heads/main/services/sensorservice/SensorService.cpp)),
  so the companion's service should keep its sensor; the feasibility
  test on the phone checks this.
- **`OrientationEventListener`:** gives the phone's angle, 0 in its
  natural position, 90 with its left side at the top, 180 upside down
  and 270 with its right side at the top, or `ORIENTATION_UNKNOWN` when
  the phone is too close to flat to tell.

### Looked at first (2026-11-13)

On the recordings to hand (measured): every one holds the display's
rotation, and only one has a rotation other than 0; none holds how the phone itself was held, so the
check cannot be measured on them, and they give "not looked for".

### As built

- **The companion app, protocol 7.** On an "orientation" request, its
  accessibility service starts an `OrientationEventListener` at
  `SENSOR_DELAY_NORMAL` and sends that connection an "orientation"
  message at once and each time the phone's quarter changes: "reason"
  ("first", "changed", or "unavailable" when the phone has no sensor to
  tell), "orientation" (0, 90, 180 or 270, or -1 for flat or unknown),
  "degrees" (the angle that placed it) and the phone's time. An angle is
  placed in a quarter only within 30 degrees of it, so a phone held
  between two keeps the last. The sensor runs only while a connection
  asks, and stops when none does. The recorder asks as it connects; a
  companion app older than protocol 7 is refused, as for each protocol
  before, so the Android device panel offers "Install companion app".
- **The record:** `android.orientation`, event type `phone-orientation`,
  at the phone's time fitted to the session clock, as for the display.
  The playback index keeps the records whole (its version is now 32, so
  older recordings are indexed again when opened). As for the phone's
  settings and trees, the records are kept in the recording file, with
  no evidence tables in the database.
- **Auto-rotate** (`system/accelerometer_rotation`) is now one of the
  phone settings read as the recording starts and stops and on each
  change (35 settings).
- **The check, "Orientation restricted (WCAG 1.3.4)",** a warning at
  both audit levels: in a visit, the phone held sideways (90 or 270)
  with the screen not turned, or held upright (0 or 180) with the screen
  turned, with Auto-rotate on, for at least the time chosen in "Phone
  turned for:". Held flat ends such a time. Each visit has at most one
  finding, on the window's first item, with a time for each such turn;
  for example "the phone was held sideways for 3.0 seconds with
  Auto-rotate on, and the screen stayed portrait, as when the app
  restricts its display to one orientation; look for whether that
  orientation is essential, as for a piano or a bank cheque".
- **Not looked for,** with the reason on the line under "Show current
  visit", when no screens were recorded, the phone has no sensor, the
  recording was made with a companion app older than protocol 7, the
  screen's rotation or the phone's settings were not recorded; and no
  finding is made while Auto-rotate is off.
- **The line under "Show current visit"** adds, for example,
  "Orientation (WCAG 1.3.4), one check: no findings; the phone was
  turned between upright and sideways 2 times, from the companion app's
  own sensor, and the screen stayed as it was, with Auto-rotate on, for
  2 seconds or more after 0 turns within a visit; it is looked for only
  while Auto-rotate is on."
- **"Phone turned for:"** in the Checks tab, beside the audit level,
  offers 1 to 5 seconds; the choice is kept with the audit level, and
  changing it runs the tree's checks again.
- **Checked before any PC test:** the solution builds with no warnings,
  and the tests pass, with the Windows-only browser tests failing as
  before. The new tests cover the message's reading, a bad orientation
  or angle refused, the collector asking and recording, and the check:
  a portrait screen with the phone held sideways for 3 seconds (a
  warning, at the turn), a screen that turned half a second after the
  phone, Auto-rotate off, a hold time of 5 seconds, and a recording
  without the records (not looked for). The companion app's Java cannot
  be built here, so it was reviewed by eye; it is built on the PC.

### Limits

- The sensor shows how the phone was held, not what the app asked
  for: a screen that stays as it was is taken as restricted, which is
  why the finding is a warning. A slow app, or a rotation Android held
  back while the screen was touched, could also stay put for a time.
- Whether an orientation is essential is not looked for.
- F100, a message asking the user to turn the phone, is not detected.
- A recording must include a turn: the test procedure needs a step that
  turns the phone and holds it, with Auto-rotate on.
- The number of turns in the summary counts every change between
  upright and sideways that the sensor reported, including brief ones,
  such as when the phone is put down; only turns held for the hold time
  give findings. In test 53 a sideways reading of under half a second,
  as the phone was put down, made the count 3 where 2 turns were held.
  The owner chose to leave the count as it is (2026-11-13).
- Reading the sensor from the companion's service while it is in the
  background was checked on the Pixel 10 Pro XL in tests 52 and 53
  (2026-11-13).

## Step 16: white space used for layout

### The owner's decisions (2026-11-13)

The candidates list first had 1.3.2 as the screen reader's order
compared with the order on the screen. The owner pointed out that this
is not 1.3.2: a reading order can differ from the screen's and still be
meaningful (out of order paragraphs fail, text and a table in either
order do not), and telling which needs an understanding of the content.
What can be found without it is white space used for layout. The owner
asked for the F32 and F33 findings, and then, as newlines used to force
layout also fail 1.3.2, for a line break in mid-sentence. The
recordings were looked at first (below), then the choices were put to
the owner:

- **F32, which letters count as a spaced-out word:** "3+ letters, all":
  a run of 3 or more single letters (2 or more Han, kana or Hangul
  characters) separated by one white space character each, a space, a
  no-break space, an ideographic space or a line break. An initialism
  spaced out, such as "U S A", which F32 exempts, is found as well, a
  false positive the owner accepts.
- **F33, what counts as columns:** "2+ lines with gaps": 2 or more lines
  of one item's text that each have a gap of 2 or more white space
  characters, or a tab, between words. One line with a double space is
  not columns.
- **A line break in mid-sentence:** "Mid-sentence too": a line break in
  one item's text where the line before ends with none of . ! ? : ; (or
  an ellipsis, or their CJK forms, with any closing quotes or brackets
  after them) and the next line starts with a lowercase letter.
- **Level:** "Both errors", at both audit levels (1.3.2 is level A),
  and the mid-sentence break an error as well.

### What the standards say (checked in cited documents)

- **1.3.2 Meaningful Sequence (A):** "it be possible to programmatically
  determine at least one sequence of the content that makes sense"; a
  sequence is meaningful "if the order of content in the sequence cannot
  be changed without affecting its meaning", and "There may be more than
  one order that is "correct"" ([Understanding 1.3.2](https://www.w3.org/WAI/WCAG22/Understanding/meaningful-sequence.html)).
  WCAG2ICT says it "applies directly as written" to non-web software
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)).
- **F32:** white space characters, "such as space, tab, line break, or
  carriage return", used "to control letter spacing within a word"; its
  examples are "W e l c o m e", "H&nbsp;E&nbsp;L&nbsp;L&nbsp;O", Tokyo
  written with an ideographic space between its two characters, and
  vertical text made with line breaks. "Inserting white space characters
  into an initialism is not an example of this failure", and white
  space between words "is not a failure" ([F32](https://www.w3.org/WAI/WCAG22/Techniques/failures/F32)).
- **F33:** white space characters used "to format columns of data in
  text content"; a screen reader reads such text across the columns,
  line by line ([F33](https://www.w3.org/WAI/WCAG22/Techniques/failures/F33)).

### Looked at first (2026-11-13)

Measured on the 45 phone recordings made since 2026-11-08, 15 of them
with trees: 493 distinct texts (items' text, content descriptions,
hints, state descriptions and pane titles), from Settings, Gmail,
Chrome, the home screen, the client's app (Nova SleepCare) and others.
None has spaced-out letters. Four Gmail texts have 2 spaces in a row,
each on one line: a sender list and its count ("me, Chris  2", with
no-break spaces) and a subject typed with a double space, neither of
them columns. The 21 texts with line breaks break between whole parts,
such as a title and its subtitle ("Sleep Goals" over "Update your
goals"), an emoji over its word, a heading over a list, or list items;
Settings starts each item of one list with a no-break space. None
breaks a sentence. The rules as built find nothing in any of the 493.

### As built

- **The checks,** `PhoneSequenceChecks`, run with the tree's checks on
  each taking of a visit's window, and their findings are merged as
  theirs are, by visit, check and item. Three discrete errors, at both
  audit levels:
  - "Letters spaced apart with white space (WCAG 1.3.2, F32)", for
    example "its text has 7 single letters separated by spaces, "W e l c
    o m e", so a screen reader may read them one at a time instead of as
    the word "Welcome"; F32: white space used to space the letters of a
    word; an initialism spaced this way, which F32 allows, is found as
    well". A line break in the run is shown as "⏎".
  - "Columns made with white space (WCAG 1.3.2, F33)", for example "3
    of the 6 lines of its text have a gap of 2 or more spaces, or a tab,
    between words, as columns laid out with white space, the first "Name
    Hours"; a screen reader reads across the columns, line by line;
    F33: white space used to make columns".
  - "Line break in mid-sentence (WCAG 1.3.2)", for example "its text has
    a line break in mid-sentence, the first between "This sentence is
    broken" and "across two lines for": the line ends with no . ! ? : or
    ;, and the next starts with a lowercase letter, so the break is there
    for its layout, and a screen reader or larger text keeps it". A break
    inside a run of spaced letters (vertical text) is F32's, not counted
    again.
- **The text looked at** is what TalkBack reads from each shown item:
  its text, content description, hint, state description and pane
  title. The text typed into a field is the user's, not the app's, so
  it is not looked at, unless the field is showing its hint. Each item
  has at most one finding of each check, from the first of its texts
  that shows it. OCR is not used: it reads the picture, where spacing
  drawn by the font cannot be told from white space in the text.
- **The line under "Show current visit"** adds, for example, "White
  space used for layout (WCAG 1.3.2), three checks: no findings, from
  12 texts looked at (each shown item's text, content description, hint,
  state description and pane title, from the tree, not OCR; text typed
  into a field is not looked at); letters of a word spaced apart (3 or
  more single letters, 2 or more Han, kana or Hangul, initialisms
  included), 2 or more lines with gaps of 2 or more spaces or a tab, and
  a line break before a lowercase letter with no . ! ? : or ; before it.
  Whether the order of the content makes sense is not looked for."
- **No new record:** the checks read the trees already recorded, so the
  playback index's version is unchanged, and recordings to hand can be
  checked.
- **A test page,** `docs/validation/fixtures/white-space-layout.txt`, a
  plain text file Chrome shows as it is, with each of the three.
- **Checked before any PC test:** the solution builds with no warnings,
  and the tests pass, with the Windows-only browser tests failing as
  before. The new tests cover F32's examples (spaces, no-break spaces,
  an ideographic space, line breaks, a letter with a combining accent,
  an initialism), what is not spaced letters (2 letters, words, digits,
  double spaces), columns against single lines with a double space and
  Settings' indented list, mid-sentence breaks against the measured
  titles, lists and emoji, and which of an item's texts are looked at.

### Limits

- Whether the order of the content makes sense, such as paragraphs out
  of order, is not looked for: that needs an understanding of the
  content, and would be a vision or AI review.
- An initialism spaced out is found, which F32 allows (owner's choice).
  Single letters that are a list, such as "A B C" for answer choices in
  one text, are found as well.
- A list of lowercase words, one to a line, with no punctuation, is
  found as a mid-sentence break.
- Columns are found only within one item's text. Columns made of items
  side by side, or a layout table, are not; nor are columns whose lines
  are separate items.
- F34, a table made with white space, is not built; text laid out as a
  table with gaps on 2 or more lines is found as F33's columns.
- Line breaks between words in a script with no case, such as Japanese,
  are not found, as the next line has no lowercase letter.

## Step 17: target-size exceptions

### The owner's decision (2026-11-16)

Offered as the next check, "Target-size exceptions. Ok, let's do that":
the exceptions of WCAG 2.5.8 and 2.5.5 are judged without a model, as
decided in `android-vision-checks.md` (decision 7, step 3), by the rule
"AI only when non-AI cannot do the test".

### What the standards ask (checked in cited documents)

- **2.5.8 Target Size (Minimum), AA:** a target of at least 24 by 24
  CSS pixels, except for spacing, an equivalent control on the same page
  that meets the size, a target in a sentence "or its size is otherwise
  constrained by the line-height of non-target text", one sized by the
  user agent, and an essential presentation
  ([Understanding SC 2.5.8](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html)).
- **2.5.5 Target Size (Enhanced), AAA:** 44 by 44 CSS pixels, with the
  exceptions of an equivalent target, a target "in a sentence or block
  of text", one sized by the user agent, and an essential presentation;
  "If the target is the full sentence and the sentence is not in a
  block of text, then the target needs to be at least 44 by 44"
  ([Understanding SC 2.5.5](https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced.html)).
- **Overlapping targets.** WCAG's definition of a target's size: "If two
  or more targets are overlapping, the overlapping area should not be
  included in the measurement of the target size, except when the
  overlapping targets perform the same action or open the same page"
  (Understanding 2.5.8). Its Figure 9 shows a small target over a
  larger one failing.

### Measured first (2026-11-16)

On the 43 recordings on the development PC, 20 of them with phone
screens, checked at AA and AAA:

- **24 dp (AA):** 2 not run, the same Settings row, 12 dp high, against
  the edge of a list that scrolls, on two visits.
- **44 dp (AAA), not run:** 5, each against the edge of a list that
  scrolls, none seen whole in another snapshot of its visit.
- **44 dp (AAA), errors:** 7. Four are the Launcher's date and weather
  text, each inside a ViewGroup of 402.1 by 103.4 dp that can be tapped.
  Two are links in a line of the client's sign-in text, each beside a
  text item of the same height that is not a target ("and"). One is a
  link alone on its line, with the nearest text 535 pixels away.
- No undersized target had another target of the size with the same
  name.

### As built

The rules are in `PhoneTreeChecks` (`TouchTargetWcag`,
`TouchTargetEnhanced`, `TextOnLine`, `EquivalentTarget`,
`LargerTargetAround`) and `PhoneCheckRun`. They are applied in this
order to each undersized target, for both checks; the check names and
their guidance are unchanged, and the ATF check (`touch-target-size`)
is left as ATF has it.

1. **A target in a line of text** passes, as information. From the
   tree, not OCR, so it is exact and repeatable: a visible item with
   text or a description holding a letter, not clickable or long
   clickable and not inside the target, whose height is within 10% or
   2 dp of the target's, which overlaps it vertically by at least 80%
   of the smaller height, and is no further from it sideways than the
   target's height. The target has text or a description, and is not
   checkable, editable, a check box or a switch, as those sit beside
   their labels without being in a sentence. Text drawn but not in the
   tree is not counted, and the finding says so.
2. **An equivalent target** passes, as information: another visible
   target, not inside the target nor around it, of at least 24 by 24 or
   44 by 44 dp, whose speakable name (`SpeakableText`, trimmed) is the
   same, ignoring case. This is inferred from the name, as the tree does
   not say what a target does, and the finding says so.
3. **Against the edge of a list that scrolls,** not run, as before.
4. **Inside another target.** For 2.5.5, a target inside a clickable
   ancestor of 44 by 44 dp (clickable as the target is, as ATF takes
   it) is a warning, not a pass. For 2.5.8, a target whose 24 dp circle
   meets only its own clickable ancestors or descendants is a warning.
   They are one target only if they do the same thing, which the tree
   does not say, so a person confirms it.
5. **Otherwise,** an error, as before.

**Seen whole.** A 24 dp or 44 dp finding not run in one snapshot, as
against the edge of a list that scrolls, is dropped when another
snapshot of the same visit, with the density known, shows the same item
as a target not against an edge, as that sighting is judged on its own.
`PhoneCheckRun.TargetsSeenWhole` counts them, and the Checks tab says
how many.

**The Checks tab** says, at both levels, which exceptions are judged and
how, and at AAA that a target sized by Android and an essential
presentation are not judged.

### A change to what was found before

Before this step, the 24 dp check counted a target's own ancestors and
descendants as the same place to touch, and passed it (see "Where the
recorder differs from ATF"). As WCAG counts them as one only when they
do the same thing, that case is now a warning. On the recordings
measured, this adds a 24 dp warning for each of the two Launcher dates,
22.6 dp high, whose circle meets the "At a glance" pager around them.

### Run on the measured recordings

- **AAA:** the two links in the line of sign-in text are information;
  the four Launcher items are warnings; the link alone on its line is
  still an error; the 5 not run stay not run.
- **AA:** the 2 not run stay; the two Launcher dates are warnings.

### Checked

The solution builds with no warnings, and the tests pass, 2,596 of
them, with the 10 that need Windows failing as before. The new tests
cover a target meeting only its clickable parent at 2.5.8, a link on a
line of text at both levels, text on another line and a check box
beside its label, an equivalent target at both sizes, a target inside a
clickable item of 44 dp, and a target cut off in one snapshot and seen
whole in another.

### Limits

- Text drawn in a picture, or in an item missing from the tree, is not
  counted as a line of text.
- A block of several lines is found only one line at a time: a target
  beside a line of a paragraph that is one item spanning lines, with a
  height unlike the target's, is not found.
- The equivalent target is judged by name only. Two controls with the
  same name that do different things, such as a "Delete" on each row,
  can be passed wrongly, which is why it is information for a person to
  judge.
- One sized by Android and not by the app, and an essential
  presentation, are not judged.

## Step 18: boundaries without a model

### The owner's decisions (2026-11-16)

Offered three rules from the measurement below, the owner chose all
three: "1,2,3 together. 2 is an error. 3 is a warning." And: "If a field
only has a bottom line then it should also get a warning related to WCAG
5.2.4 as users on large magnification moving down the page may not
immediately understand that the have found an editable field as the
bottom line may be outside of the magnification window".

1. **No model for boundaries.** The boundary info findings stay as they
   are, judged without a model.
2. **A text field's border, bottom line or background below 3:1 is an
   error,** whatever text or icon it shows. From the video, a ratio
   within 12% of 3:1 is a warning, as for the other controls.
3. **Text OCR read counts as a control's own only when a word of it is
   in the item's text in the tree.** Otherwise the finding is a warning:
   the picture may not show the item, and whether it needs a boundary is
   not judged.
4. **A text field shown only by a bottom line is a warning,** whatever
   the line's contrast, in a check of its own,
   `field-bottom-line-only`, related to WCAG 2.2's conformance
   requirement 5.2.4 and to 1.4.11.

### What the standards say (checked in cited documents)

- [Understanding SC 1.4.11](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html):
  figures 31 and 32 show that a text field's indicator, a bottom border
  or a full border, needs 3:1; figure 34 that a field shown only by its
  background needs 3:1 for the background; and figure 45 that a field
  with no label fails when its border is below 3:1. The document does
  not say that a placeholder identifies a field.
- [WCAG 2.2 conformance requirement 5.2.4](https://www.w3.org/TR/WCAG22/#cc4),
  Only Accessibility-Supported Ways of Using Technologies, is a
  conformance requirement, not a success criterion, so the new check
  lists no criterion it fails and gives 5.2.4 and 1.4.11 as related.

### Measured first (2026-11-16)

On the 20 recordings with phone screens on the development PC, with
OCR, the boundary findings were listed and each was asked of Claude
Haiku 5.5 twice, "is the boundary needed to identify this control?",
with the control's picture (measured):

- 955 info findings: 560 with text, 395 with an icon of 3:1; 916 from
  the video's frames.
- Haiku said "identified without a boundary" on both runs for 908
  (95%), "boundary needed" on both for 2, and differed or could not tell
  for 45, mostly where the evidence was poor: bounds off the item, the
  video behind the tree, or OCR reading garbage.
- 1,910 requests cost about US$0.45, about 1,650 input tokens and 2.3 s
  each.
- In 150 of the 560 findings with text (27%), no OCR word was in the
  item's text in the tree; Haiku still said "identified" for 135 of
  them, so the model confirmed the wrong evidence.
- 14 info findings were text fields, which needed 3:1 whatever text they
  showed (figures 31, 32 and 34).

### As built

In `PhoneNonTextContrastRun` (`Recorder.Session`):

- **Fields.** `Judge` judges an editable control by `JudgeField`, before
  any text or icon: not run when no side was measured; otherwise the
  ratio is the bottom line's when the field is shown only by one, else
  the boundary's weakest side, judged by `Verdict` (an error below 3:1,
  a warning near it from the video). Fields are not judged as showing a
  state.
- **A bottom line only.** `BottomLineOnly` is true for an editable
  control whose bottom side has a drawn shape, whose top side was
  measured, and whose other measured sides have nothing drawn.
  `JudgeBottomLine` gives a warning under `field-bottom-line-only` with
  the line's colours and ratio. Findings are keyed by check, so a field
  can have both.
- **OCR against the tree.** `TreeText` joins the text, description,
  hint and state description of the item and its visible descendants.
  `SharesAWord` lowers both and splits them into words of 2 or more
  letters or digits; they match when a word is the same, or when a word
  of 3 or more characters is inside the other, so OCR's "ouTube" matches
  "YouTube". Text found without OCR comes from the tree and needs no
  match.
- **Passed** counts 1.4.11 only.
- **Guidance.** `android-check-guidance.json` has the new check, impact
  medium, with criterion "5.2.4" at level "Conformance requirement" and
  its source, and the 1.4.11 basis says the field and OCR rules.
- **The Checks tab** counts the two checks apart and says the field and
  OCR rules.

### Checked

The solution builds with no warnings, and the tests pass, 2,607 of
them, with the 10 that need Windows failing as before. The new tests
cover a bottom line of 3:1 (passed, with the warning), one below 3:1
(an error and the warning), a field shown by a pale background (an
error) and one with a full border (no finding), OCR text not in the
tree's text, OCR text matched by part of a word, an item with no text in
the tree, and the word matching.

### Limits

- The bottom line is found only within 16 pixels of the bounds, as for
  every side; a line drawn further in is not found.
- A field whose bounds include its label above can be measured with
  the label's area as its top, so a field with a line may not be found
  as having only a line.
- A word shared by chance, such as "Settings" in a row and its title,
  still counts as the control's text.

## Step 19: text shown as a heading

### The owner's decision (2026-11-16)

Offered four rules from the measurement below, the owner chose: "Build
rules 1 to 4 for the heading check as you described".

1. **Candidates, without a model.** An item's text, from the tree and
   placed on the picture by OCR, in an item of at most 2 lines and 10
   words with a letter in it, that is not marked as a heading, not
   inside one and holding none, and either (a) whose letters are at
   least 1.35 times as tall as the screen's body text, or (b) that is
   one line of at most 6 words, all in capitals, with no digit, not
   directly above a field.
2. **The judgement.** Each candidate is a closed question for the
   project's model for vision-based checks, with the screen and the
   text outlined: is the text shown as a heading for the content after
   it? The answer is heading, not a heading, or cannot tell. With no
   model chosen, each candidate is a warning for a manual check.
3. **Levels, as for images of text.** "Heading" agreed on both runs is
   an error under 1.3.1, marked as the model's inference; "not a
   heading" agreed is information; anything else is a warning.
4. **Text inside an item that can be tapped, or a field, is left out,**
   as a control's text is its name, and the Checks tab says how many.

This is a finding of its own, `text-shown-as-heading`, apart from the
screen's "No heading" check: that one is about the whole screen, this
one about each piece of text, so a screen can have both.

### What the standards say (checked in cited documents)

- [WCAG 2.2 failure F2](https://www.w3.org/WAI/WCAG22/Techniques/failures/F2)
  is a failure of 1.3.1 where a change in the look of text conveys
  information, such as a heading, without the markup or text that
  conveys it. Android's markup for a heading is
  `accessibilityHeading`, and TalkBack moves by headings.

### Measured first (2026-11-16)

A probe read the 20 recordings with phone screens on the development
PC, with OCR: 1,865 lines of text, 1,745 of them from the video's
frames (measured).

- One app had 423 of the lines and none marked as a heading; the other
  apps had 85 lines marked as headings, 52 of them in Android Settings.
- **Size.** The screen's body text was taken as the letter height that
  most of its characters have. At 1.35 times that or more, the only
  candidates were in the one app, 10 different texts. At 1.2 times, an
  email's body text and a card's description lines qualified as well,
  which is wrong.
- **Larger is not a heading.** Android Settings' marked headings were
  the size of the body text or smaller, and stood out by colour; and
  some large text was a message, not a heading. So the rules find
  candidates, and the model or a person judges them.
- **Capitals.** Short lines in capitals were the one app's section
  labels, the size of the body text. The same rule caught two fields'
  labels, directly above their fields, so a line in capitals within
  twice its height above a field is left out as the field's label (an
  assumption, not measured further).
- **Bold could not be measured.** The stroke from the video was 2 to 8
  pixels thick, and tab labels measured bolder than the headings.
- **Only the tree's text confirmed by OCR is reliable.** As with
  boundaries (step 18), words OCR read on their own often belonged to
  another item, as the video lags the tree.
- **Text in items that can be tapped** was a large group: 86 lines in
  the one app, 8 in the launcher, 3 in Settings, 2 in Gmail and 1 in
  Chrome, such as app labels and card titles.
- With both tests, about 19 different texts were candidates, all in
  the one app. The model's answers to the question were not measured
  before it was built.

### As built

- **The letters' height.** `PhoneColours.InkHeight` gives the rows of
  a line's box from the first to the last with a pixel nearer the
  text's colour than the background's (Delta E, as for the colours).
  The contrast measurement (step 4b) gives it to each line with
  colours, as `PhoneTextLine.InkHeight`, in the picture's pixels, so a
  ratio of two lines on the same picture does not depend on its scale.
- **The body text.** `PhoneHeadingLookRun.BodyHeight` is the median of
  a snapshot's lines' heights, each line counted once for each
  character OCR read in it.
- **The rules.** `PhoneHeadingLookRun` (`Recorder.Session`) reads each
  settled snapshot OCR read, and each item with lines placed by OCR
  (`AppTextPlacedByOcr`). It leaves out an image, an item marked as a
  heading, inside one or holding one, an item with no letter, of more
  than 2 lines or of more than 10 words. Words are runs of letters,
  digits and apostrophes. Of the rest, (a) the tallest measured line is
  at least 1.35 times the body text, or (b) the item has one measured
  line, which OCR read as at least 3 letters, all capitals, in at most
  6 words, with no digit there or in the app's text, as OCR reads "0"
  as "O". A candidate inside an item that can be tapped or long
  pressed, or a field, is counted in `InControls`; a line in capitals,
  not taller, with a visible field starting below its top, within twice
  its height of its bottom and overlapping it across, is counted in
  `FieldLabels`. Each is counted once in a visit.
- **The finding.** A warning, one for each item in a visit, with a time
  for each snapshot it was found on, saying how much taller its letters
  are (or that it is in capitals), in how many lines, that it is not
  marked as a heading, that whether it is shown as a heading is not
  judged, and whether the picture was the phone's screenshot or the
  video's frame.
- **The question.** `AndroidHeadingLook` (`Recorder.Analysis`), as for
  images of text (`android-vision-checks.md`, "Images of text, as
  built"): picture A is the whole screen at half size with the text's
  lines outlined in cyan; picture B is the screen across, at full size,
  from a line's height above the text to 30% of the screen's height
  below it, with the text outlined. The question gives the app, the
  screen's name and the item's text, says it is not marked as a
  heading, and asks for "heading", "not-heading" or "cannot-tell" with
  a one-sentence reason. Template `android-text-shown-as-heading`,
  version 1; the step key is made from the request's SHA-256, so an
  answer is kept by its request and reused. It is asked twice, or once
  for a local model at temperature 0.
- **The levels.** Agreed "heading": an error, the model's inference
  that the text should be marked as a heading. Agreed "not-heading":
  information, not confirmed. Agreed "cannot-tell", or two answers:
  a warning to check manually, with both reasons. A finding with no
  answer stays a warning, and the check's note says why.
- **Where it runs.** The Android report, the project report and the
  model checks ask after the images-of-text check; the model checks'
  Markdown has a section of its own. The Checks tab shows the finding
  under each visit, with the pieces of text placed by OCR, and the
  summary gives the findings, the rules, how many were left out and
  how many are for the model. "Ask the project's model" is enabled
  while an image or a piece of text has no answer from the project's
  model.
- **Guidance.** `android-check-guidance.json`, version 3, has the
  check, impact medium, criterion 1.3.1, with WCAG2ICT and failure F2
  among its sources, so a finding in the report follows the template
  of every finding (`android-report.md`, "Each finding").

### Checked

The solution builds with no warnings, and the tests pass, 2,616 of
them, with the 10 that need Windows failing as before. The new tests
cover made-up text: a large title and a line in capitals found; a
field's label, a button's text, a marked heading, a line in capitals
with a digit, three lines and eleven words left out; no OCR; the
answers judging the findings; the body text's height; the letters'
height; the request's key and pictures; kept answers; and what the
answers make of a finding.

### Limits

- Bold, colour and place are not measured; a heading the size of the
  body text, not in capitals, is not found, as Android Settings'
  headings would not be if they were unmarked.
- The letters' height is the line's from the top of its tallest letter
  to the bottom of its lowest, so a line with no tall or low letters,
  such as "now", measures smaller than its size.
- The 2 times reach of a field's label is an assumption.
- Text in a control that is shown as a heading is not found.

## Step 20: images with no text alternative

### The owner's decision (2026-11-16)

Asked for "An informative image with no description", then offered
five rules from the measurement below, the owner chose: "Build the
image check with the proposed rules 1 to 5".

1. **Drawn under or over controls.** An image at least half of whose
   area is under or over controls with a name, that neither hold it nor
   are inside it, is not checked.
2. **Lines.** An image under 2 dp on its shorter side is not asked
   about, where the recording holds the density.
3. **Pictures out of step.** An image is not asked about on a picture
   that does not match the tree at that moment, as the images-of-text
   check finds it (step 8, rule 2).
4. **One answer for one image.** The answer is kept against the
   image's own pixels and the text beside it, so an identical image is
   given the first answer again, across visits and recordings in the
   project.
5. **Levels.** Informative is an error, the model's inference, high
   impact; cannot tell is a warning for a manual check; decorative is
   information, as for the other model checks.

This is a finding of its own, `image-no-text-alternative`, apart from
the tree's "Item with nothing for a screen reader to say"
(`speakable-text-present`): that one is about a control, which needs a
name whatever it looks like; this one is about an image that is not a
control, which needs a text alternative only when it gives
information.

### What the standards say (checked in cited documents)

- [WCAG 2.2's 1.1.1](https://www.w3.org/WAI/WCAG22/Understanding/non-text-content.html)
  asks for a text alternative for non-text content that gives
  information, and for decoration to be implemented so assistive
  technology can ignore it. Whether an image gives information is a
  judgement of what it shows, so the rules find the images a screen
  reader says nothing for, and the model or a person judges them.

### Measured first (2026-11-16)

A probe read the 20 recordings with phone screens on the development
PC: 576 images (ImageView or a class drawn from one, visible) on the
pictures OCR read (measured).

- Most had a name of their own, were controls, or were inside a
  control with a name. 49 had none of these, on 8 recordings.
- **Rule 1.** 32 of the 49 were drawn under controls with a name: 28 of
  the launcher's views under the sheet of all apps, 3 of Chrome's menu
  badges and 1 of Chrome's full-screen images, 75% covered.
- **Rule 2.** 1 was a Chrome hairline 0.82 dp high. Two hairlines on
  older recordings with no density recorded were asked about, and
  answered decorative.
- **Rule 3.** 2 were the launcher's weather images on the home screen,
  whose picture, a video frame, showed Gmail opening; the model
  described Gmail's rows.
- **The answers.** The 16 left were asked of Claude Haiku 5.5 twice;
  each pair agreed and none was cannot tell. Informative: an app's
  illustration, 3 Settings illustrations, the Google logo, 4 of 6
  "i" icons and the 2 weather images; decorative: a "G" icon, the 2
  hairlines and 2 of the 6 "i" icons. The 2 runs used 63,164 input and
  3,234 output tokens.
- **Identical images, different answers.** The 6 "i" icons were the
  same 59 by 59 pixel image (`android:id/icon`), some on the same
  screen with the same text beside them, and were split 4 to 2. Their
  pictures differed only by the screen round them, so their requests
  differed. On 16 by 16 grey thumbnails of the icon alone they differed
  by at most 3 of 255, including a video frame against a screenshot,
  while a hash of the exact or rounded pixels differed. Rule 4 follows
  from this.

### As built

- **The rules.** `PhoneImageAlternativeRun` (`Recorder.Session`) reads
  each settled snapshot OCR read, as rule 3 needs OCR's words, and each
  visible image with bounds. It leaves out, in turn: an image with a
  content description, text or label (`Named`); an image that is a
  control, visible, important for accessibility and clickable, long
  clickable, focusable or focusable by a screen reader, whose name the tree's checks judge
  (`Controls`); an image whose nearest control holding it has a name
  (`InNamedControls`); rule 1 (`Covered`), the share of the image's
  area under controls with a name that neither hold it nor are inside
  it, sampled on a grid of at most 60 points along its shorter side;
  rule 2 (`Thin`), with the density at the snapshot's taking. Each is
  counted once in a visit.
- **Rule 3.** `PhoneImagesOfTextRun.OutOfStepAt` gives the
  images-of-text check's test to this check: at least 3 lines OCR read
  outside every image, in no item's text and not TalkBack's caption,
  more than the text items OCR found where the tree places them. An
  image on such a picture is a check not run, saying why, only where no
  picture of the visit is in step.
- **The finding.** A warning, one for each image in a visit, with a
  time for each snapshot it was found on, giving the image's size in
  pixels and dp, that it has no description, text or label and no
  control with a name holds it, so a screen reader says nothing for
  it, that whether it gives information is not judged, and whether the
  picture was the screenshot or the video's frame.
- **The text beside it.** What a screen reader would say for each of
  the image's parent's other children, joined, or where none has text,
  its grandparent's; at most 400 characters.
- **What a screen reader says on the screen.** Added on 2026-11-16 (see
  "What a screen reader says, as context" below): each item a screen
  reader would stop on, by the tree's checks' rule
  (`PhoneTreeChecks.ShouldFocusView`, from ATF), with what it would say
  for it, in the tree's order, joined with " | ", with "[the outlined
  image]" at the image's place. Longer than 4,000 characters, the items
  nearest the image's place are kept, and "..." marks what is left out.
- **The question.** `AndroidImageAlternative` (`Recorder.Analysis`), as
  for images of text: picture A is the whole screen at half size with
  the image outlined in cyan; picture B is the image and 48 pixels round
  it, enlarged up to 4 times so its shorter side is at least 192
  pixels, no longer than 1,024, outlined 4 pixels wide. The question
  gives the app, the screen's name, the text beside the image and what
  a screen reader says on the screen, says it has no text alternative
  and that text seen in the pictures but not in what a screen reader
  says is not given to a screen reader user, and asks for "informative",
  "decorative" or "cannot-tell" ("such as when nothing can be seen
  inside the outline") with a one-sentence reason. Template
  `android-image-text-alternative`, version 2; the step key is made
  from the request's SHA-256. It is asked twice, or once for a local
  model at temperature 0.
- **Rule 4, the signature.** Each request carries a signature, kept
  with the attempt in `analysis_attempts.input_signature` (migration
  0047): a version, the image's width and height in pixels, the
  SHA-256 of the text beside it, the SHA-256 of what a screen reader
  says on the screen with the image's place marked (signature version
  2, see below), and a 16 by 16 colour thumbnail of the image's own
  pixels, each cell the mean of the pixels it covers. Two
  are the same image when all but the thumbnail are equal and every
  channel of the thumbnails is within 16 of 255. Before asking, the
  check looks for answers with a signature by the same profile and
  model on the project's tasks and its recordings' tasks
  (`AnalysisCatalog.SignedAnswersAsync`), or for a recording in no
  project, on the recording's; the earliest for the same image is
  copied into the task, with both runs where the model is not
  repeatable, and the finding says the answer was first given for the
  same image. An image the same as one already answered in the same
  run takes that answer. The Checks tab reads the same answers.
- **The levels.** Agreed "informative": an error, the model's
  inference that the image needs a text alternative. Agreed
  "decorative": information, not confirmed. Agreed "cannot-tell", or
  two answers: a warning to check manually, with both reasons. A
  finding with no answer stays a warning, and the check's note says
  why.
- **Where it runs.** The Android report, the project report and the
  model checks ask after the check of text shown as a heading; the
  model checks' Markdown has a section of its own. The Checks tab shows
  the finding under each visit, and the summary gives the findings, the
  rules and how many each left out. "Ask the project's model" is
  enabled while an image has no answer from the project's model.
- **Guidance.** `android-check-guidance.json` has the check, impact
  high, criterion 1.1.1, with remediation for Views, Compose, Flutter
  and React Native: a description for an image that gives information,
  and hiding a decorative one from accessibility services.

### Checked

The solution builds with no warnings, and the tests pass, 2,627 of
them, with the 10 that need Windows failing as before. The new tests
cover made-up items: an image a screen reader says nothing for found;
an image with a description, an image that is a control, an image in
a control with a name, a background under a button and a hairline left
out; the hairline asked about with no density; the share covered; no
OCR; a picture out of step; the answers judging the finding; the
request's key, pictures and signature; the signature's tolerance and
what differs; what a screen reader says on the screen, and on a long screen what is kept round the image; kept answers from another task, needing both runs for a
model that is not repeatable; what the answers make of a finding; and
the signature kept with an attempt, copied when reused and found by
project, recording, template, version and model.

### Limits

- Only images in the tree are checked. An image drawn by a framework
  with no node, such as a Flutter image with no semantics, is not.
- An image that is a control is left to the tree's checks of names,
  so a control with an informative image and a wrong name is not this
  check's finding.
- The thumbnail is coarse: two images of the same size, with the same
  text beside them, that differ only in fine detail could be taken as
  one. The tolerance is from 6 icons, not measured further.
- The same image with different text beside it is asked again, as the
  text can change what it means.
- Rule 1's grid and its threshold of half are from the measurement, not
  measured further.
- What a screen reader says is the tree's order, not the order a screen
  reader moved in on the recording, and only the app's window.
- Rule 4 matches what a screen reader says exactly, so the same image
  on the same screen is asked again when anything said changes, such as
  a time, a count or a list scrolled by one row.
- As the image's place is marked, identical images in different places
  on one screen are asked separately, as the 6 "i" icons of the
  measurement would be; rule 4 now gives the first answer again for the
  same image in the same place on a screen that says the same, on
  another visit or recording.

### What a screen reader says, as context (owner, 2026-11-16)

Asked how the check tells that an image is informative in its context
of use, and told that the model was given only the text beside the
image and judged information against "the text on the screen", which
can include text a screen reader does not reach, the owner chose: "Pass
the screen reader's text for the whole screen to the image check".

- The question gives what a screen reader says on the screen, with the
  image's place marked, and asks whether the image gives information
  that this does not. Text seen in the pictures but not said is
  stated not to be given. The text beside the image is still given.
- The template is version 2, so answers to version 1 are not reused,
  and each image is asked again.
- The limit of 4,000 characters was chosen, not measured.
- Then, told that rule 4 still matched an image by its pixels and the
  text beside it alone, so the same image on a screen that says other
  things would be given the first answer, the owner chose: "Pass the
  screen reader text to the image check's rule 4 matching too". The
  signature (version 2) adds the SHA-256 of what a screen reader says
  on the screen, with the image's place marked, so an answer is given
  again only for the same image where a screen reader says the same.
- On 2026-11-17 the reason was asked for before the answer, for every
  check's question (`android-vision-checks.md`, "Keeping it
  repeatable", point 7). The template is version 3, so answers to
  version 2 are not reused.

## Step 21: icons and descriptions

### The owner's decision (2026-11-17)

Asked to build the description-matches-image check, then offered eight
rules from the measurement below, the owner chose: "Build rules 1 to 8
for the icon and description checks as proposed". Earlier, told that
an icon whose name it contradicts could be found, the owner had said:
"That sounds a good idea, just warning with recommended human
verification. We will save testing charts and captions until I have
content that requires it".

1. **Two findings, one issue each.** "Icon that contradicts its
   control's name" (`icon-contradicts-name`), for an image that is a
   control, and "Description that does not match its image"
   (`description-not-matching-image`), for an image that is not a
   control but has a description of its own. Both are WCAG 1.1.1.
   2.5.3 is not given: Label in Name is about a visible text label, and
   an icon has no text.
2. **Which images.** An ImageView or a class drawn from one, visible,
   important for accessibility, with a content description or text of
   its own.
3. **Pictures out of step.** An image on a picture out of step with the
   tree is not asked about there, as for the image check (step 20,
   rule 3).
4. **The kind question first, for controls.** A control's image is
   asked what kind of image it is, and its name is judged against it
   unless both answers agree it is not an icon (a photograph, an
   illustration, a logo, a chart, text or a decoration).
5. **The questions as measured, reason first,** with what a screen
   reader says on the whole screen and the image's place marked. The
   icon question's answers are fits-the-name,
   symbol-for-something-else and cannot-tell; the description
   question's are matches, does-not-match and cannot-tell.
6. **One answer for one image.** The answer is kept against the image's
   own pixels, its name or description, and what a screen reader says
   on the screen, so the same image is given the first answer again.
7. **Levels.** A contradiction or a mismatch is a warning recommending
   that a person verify it, high impact if confirmed. Cannot tell, or
   two answers that are still two after the model for complex
   questions, is a warning to check manually. Fits or matches is no
   finding, and is counted as judged to fit, not confirmed.
8. **No model.** With no model for vision-based checks, there is no
   finding for each image, and the counts say how many were not
   judged.

Left out, for later: a decorative image that is announced (its own row
in `android-vision-checks.md`, built as step 23); whether a description gives the
image's purpose; charts and captions, until there is content that
needs them.

### What the standards say (checked in cited documents)

- [WCAG 2.2's 1.1.1](https://www.w3.org/WAI/WCAG22/Understanding/non-text-content.html)
  asks that a text alternative serve the equivalent purpose of the
  non-text content, and that a control's be its name, describing its
  purpose. An icon that is the usual symbol for something other than
  its name, or a description of something an image does not show,
  gives a person who sees it and a person who hears it different
  things.

### Measured first (2026-11-17)

A probe read the 20 recordings with phone screens on the development
PC. Rule 2 chose 148 images, 86 of them controls and 62 not (measured).
The probe asked about 51 different images, 44 controls and 7 not.

- **The kind question.** Both answers agreed for all 51: 43 icons, 5
  illustrations and 3 logos. 6 of the 44 controls were logos or
  illustrations, such as cards, so their names were not judged against
  them.
- **The icon question, Claude Haiku 5.5, reason first.** Of the 38
  controls asked, 36 fit their names in both answers. Chrome's update
  arrow, named "Update available. More options", was given two
  answers on one screen, and the account initial, a letter in a
  circle, was cannot tell. A second run gave the same answers for 110
  of 116 question pairs; the 6 that changed were these borderline
  images.
- **The description question.** 6 of the 7 matched. Haiku answered
  that "Colour inversion animation" does not match a still
  illustration of chat bubbles, as no animation can be seen.
- **Claude Sonnet 5.5** judged all 38 icons to fit and all 7
  descriptions to match, every pair agreeing, including Chrome's
  update arrow and the account initial.
- **Cost.** 220 requests cost about US$0.055 with Haiku and US$1.074
  with Sonnet, at the models' prices. So Haiku is asked first, and a
  question it does not settle is asked again of the model for complex
  questions where the project chooses one.
- No real defect was found in the sample, so finding a contradiction
  or a mismatch is not yet measured; it waits for test content made to
  have them.

### As built

- **The rules.** `PhoneDescribedImageRun` (`Recorder.Session`) reads
  each settled snapshot OCR read, and each visible image with bounds,
  important for accessibility, with a content description, or where it
  has none its own text (rule 2). It is a control where it is
  clickable, long clickable, focusable or focusable by a screen reader.
  An image on a picture `PhoneImagesOfTextRun.OutOfStepAt` finds out of
  step is counted (`OutOfStep`) and not asked about (rule 3). Each
  image is counted once in a visit (`Images`, `Controls`), and asked
  about once in a visit, with a time for each snapshot it was seen on
  in step.
- **What a screen reader says for it.** The image, where a screen
  reader stops on it (`PhoneTreeChecks.ShouldFocusView`), or else the
  nearest item holding it that a screen reader stops on, with what it
  says there. What a screen reader says on the screen is given as for
  the image check, with "[the outlined image]" at the image's place.
- **The questions.** `AndroidDescribedImages` (`Recorder.Analysis`)
  gives picture A, the whole screen with the image outlined, and
  picture B, a close-up enlarged when the image is small, as the image
  check does. The question gives the app, the screen, the image's name
  or description, which item a screen reader says it for and what it
  says there, and what a screen reader says on the screen, then asks
  one of three questions, each ending "First write one sentence saying
  what in the pictures shows the answer, then give the answer.":
  `android-image-kind`, `android-icon-name` and
  `android-image-description`, each version 1, with step keys made
  from the request's SHA-256.
- **Rule 4.** A control's kind question is asked first. Where both of
  the deciding model's answers agree on a kind other than icon or
  cannot tell, the control is counted as not an icon (`NotIcons`), as
  information, and its icon question is not asked.
- **Rule 6, the signature.** As the image check's, with the image's
  pixels, its size, what a screen reader says on the screen, and in
  place of the text beside it, whether it is a control and its name, or
  for an image that is not a control its description, the item a
  screen reader says it for and what it says there. A control's kind
  and icon questions share the signature, kept apart by their
  templates. An answer kept on a task of the project, by the same
  profile and model, is copied into the task; the same image in the
  same run takes the answer already given.
- **The model for complex questions.** A question whose two answers
  differ, or are cannot tell, is asked again of the model for complex
  questions where the project chooses another (runs 3 and 4), and its
  answers decide the finding.
- **Rule 7, the levels.** Agreed symbol-for-something-else or
  does-not-match: a warning, the model's inference, ending "a person
  should verify this". Agreed cannot-tell, or two answers: a warning
  ending "check manually". Agreed fits-the-name or matches: no
  finding, counted (`Fit`). Every answer is the model's inference, so
  the report says AI was used even where no finding was made
  (`AndroidAudit.Inferences`).
- **Rule 8.** Before any answer, and with no model, the run makes no
  finding; `Unanswered` counts the questions, and the note says why.
- **Where it runs.** The Android report, the project report and the
  model checks ask after the image check; the model checks' Markdown
  has a section, "Icons and descriptions", and the report's limits give
  the check's note. The Checks tab gives a row for each of the two
  checks under each visit, and the summary gives the findings, the
  images, the controls, those out of step, those judged to fit, those
  not icons and those not judged. "Ask the project's model" is enabled
  while a described image is not judged.
- **Guidance.** `android-check-guidance.json` has both checks, impact
  high, criterion 1.1.1, with remediation for Views, Compose, Flutter
  and React Native: a name that says what the control does with the
  icon commonly used for it, and a description that says what the
  image shows or stands for.

### Checked

The solution builds with no warnings, and the tests pass, 2,642 of
them, with the 10 that need Windows failing as before and one timing
test of the Android companion that failed under load and passes on its
own. The new tests cover made-up items: an icon that is a control, an image with a
description inside a row a screen reader stops on, an image with no
description and an image not important for accessibility; no OCR; a
picture out of step; findings only from warnings, with the rest
counted; the requests' keys, answers and text; the shared signature of
a control's two questions; kept answers judging a logo as not an icon,
a symbol for something else and a mismatch as warnings, and a kind
with one run of two not yet an answer; and the levels of each answer
and of two answers.

### Limits

- Only images in the tree are checked, as for the image check.
- A control whose image is text, such as an initial in a circle, is
  asked the icon question unless both answers agree it is text.
- Whether a description gives the image's purpose, rather than naming
  what it shows, is not asked.
- No contradiction or mismatch was in the measured sample, so how well
  the check finds one is not measured.
- What a screen reader says is the tree's order, not the order a screen
  reader moved in on the recording.

## Step 22: a screen title that does not describe its screen

### The owner's decision (2026-11-17)

Asked what came next after the icon and description checks, the owner
noted that every screen of one app has the same title, and asked for
the check to be added to the existing 2.4.2 testing. Offered eight rules and two ways
to treat Chrome from the measurement below, the owner chose: "Add the
screen title does not describe its screen check with rules 1 to 8 and
Chrome option A".

1. **One issue.** "Screen title does not describe its screen"
   (`screen-title-not-descriptive`), WCAG2ICT 2.4.2, a third issue
   beside "No screen title" and "Screen title shared with other
   screens". A screen can have both a shared title and this one, as
   their remedies differ.
2. **Which screens.** Each titled screen once, from its first settled
   snapshot OCR read. An untitled screen is left to "No screen title".
3. **No model where the title is the screen's own text.** A title that
   is exactly a heading shown on the screen, a whole line of the text
   OCR read, or all that a screen reader says for one item, comparing
   only letters and digits in lower case, describes its screen with no
   model asked. A title only within such text, such as "Settings" in
   "Search Settings", goes to the model.
4. **What the model is given.** The title, what a screen reader says on
   the whole screen and the text OCR read, each marked with where it
   came from, and the screen's picture where images cover at least half
   of the screen.
5. **Chrome, option A.** Chrome's screens are included, and the part of
   the title after "Chrome: ", the web page's title, is judged.
6. **Levels.** A title judged not to describe its screen is a warning
   for a person to verify, medium impact if confirmed, as the other
   2.4.2 issues.
7. **No model.** With AI off, or no model chosen, a screen that needs
   one is counted as not checked, and no finding is made.
8. **One answer for one screen.** A saved answer is reused only where
   the title, what a screen reader says, the OCR text and, where given,
   the picture are unchanged.

### What the standards say (checked in cited documents)

- [WCAG 2.2's 2.4.2](https://www.w3.org/WAI/WCAG22/Understanding/page-titled.html)
  asks for titles that describe topic or purpose, and
  [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) applies it to a
  software's screens. A title shared by many screens, or one that is
  only the app's name, can fail to say which screen a person is on.

### Measured first (2026-11-17)

A probe, with no model, read the 20 recordings with phone screens on
the development PC: 50 screens, all titled, 17 sharing a title in
their app, none untitled.

- **Rule 3 passed 16 of the 50** by an exact match: Settings' own
  screens, TalkBack's screens, and a mail app's "Primary".
- **34 would go to the model:** the main Settings screen ("Settings",
  matched only within "Search Settings"), the launcher's "Home" and
  "Apps list", Chrome's "Chrome: " and the page's title, and one app's
  16 screens, all titled with the app's name as the window title.
- **Images.** 5 screens were at least half images, Chrome's new tab at
  88% and one app's library screen at 60%; the next highest was 29%.
  So the picture is given only from half the screen.
- **The app's name.** App labels are not recorded, so a title that is
  only the app's name cannot be found without a model; the package
  name is given to the model.

### As built

- **The rules.** `PhoneScreenTitleRun` (`Recorder.Session`) reads each
  settled snapshot OCR read (`SnapshotsRead`) and takes each screen
  once, at its first such snapshot (`Screens`). The title is
  `PhoneScreenChecks.ScreenTitle`'s, the first pane title or else the
  window title, with where it came from. A screen with none is counted
  (`Untitled`). `PageTitle` gives Chrome's web page title, for
  `com.android.chrome` and its beta, dev and canary packages (rule 5).
- **Rule 3.** `Match` compares the judged title, letters and digits in
  lower case, with the shown headings' speakable text, then each line
  OCR read, then each item a screen reader stops on; a match is counted
  (`Matched`) with which it was.
- **Rule 4.** What a screen reader says is each item
  `PhoneTreeChecks.ShouldFocusView` stops on with text, in the tree's
  order, joined by " | "; the OCR text is the lines from the top, joined
  by " / "; each is cut to 4,000 characters from its start. The share
  of the screen images cover is sampled on a 60 by 60 grid over the
  root's bounds, from visible ImageViews; from 0.5 the question is
  pictured (`PicturedQuestions`), else text only (`TextQuestions`).
- **The questions.** `AndroidScreenTitles` (`Recorder.Analysis`) asks
  `android-screen-title`, with no picture, of the project's model for
  text-based checks, and `android-screen-title-pictured`, with picture
  A, the whole screen at half size with nothing outlined, of its model
  for vision-based checks, each version 1, with step keys made from the
  request's SHA-256. The question gives the app's package, the title and
  where a screen reader is given it, for Chrome the page's title, what a
  screen reader says and the OCR text, noting that OCR may misread, and
  asks whether the title describes the screen: describes-the-screen,
  does-not-describe (such as only the app's name on a screen with a
  purpose of its own, a word that would fit any screen, or the title of
  a different screen) or cannot-tell, reason first.
- **Rule 8, the signature.** As the image check's: for a pictured
  question, the whole screen's pixels; for a text question, the same
  layout with no size or thumbnail
  (`AndroidImageAlternative.TextSignature`), so only the same texts
  match. The text is where the title came from, the title, the page's
  title and the OCR text, and what a screen reader says. An answer kept
  on a task of the project, by the same profile and model, is copied
  into the task. Asking with a signature is shared with the icon and
  description checks (`SignedAsker`).
- **The model for complex questions.** As for the other checks, a
  question not settled is asked again of the model for complex
  questions of the same kind, text or vision, where the project chooses
  another.
- **Rule 6, the levels.** Agreed does-not-describe: a warning, the
  model's inference, ending "a person should verify this". Agreed
  cannot-tell, or two answers: a warning ending "check manually".
  Agreed describes-the-screen: no finding, counted (`Described`). Every
  answer is the model's inference, so the report says AI was used even
  where no finding was made.
- **Rule 7.** Before any answer, and with no model for a question's
  kind, no finding is made; the note says how many were not judged and
  why, for text-based and vision-based checks apart.
- **Where it runs.** The Android report, the project report and the
  model checks ask after the icon and description checks; the model
  checks' Markdown has a section, "Screen titles", and the report's
  limits give the check's note. The Checks tab gives a row under each
  visit: the finding, or that the screen was checked on another visit,
  has no title, has a title that is its own text, was judged to
  describe it, or is not yet judged; and the summary gives the counts.
  "Ask the project's model" is enabled while a title is not judged and
  the project can use the model for its kind.
- **Guidance.** `android-check-guidance.json` has the check, impact
  medium, criterion 2.4.2, with remediation for Views, Compose, a web
  page in Chrome, Flutter and React Native.

### Checked

The solution builds with no warnings, and the tests pass, 2,662 of
them, with the 10 that need Windows failing as before. The new tests
cover made-up screens: a title matching a heading, a heading with other
punctuation, an OCR line or a spoken item, and a title only within
them; an untitled screen; what a screen reader says and the OCR text
given; the image share under and over half; no OCR; Chrome's page
title; findings only from warnings, with the rest counted; the
requests' use, template, picture and signatures, the same texts being
the same screen; kept answers judging a warning and a title that
describes its screen; no kept answers and no model; and the levels of
each answer and of two answers.

### Limits

- The model judges from the package name, not the app's label, which
  is not recorded.
- Each screen is judged on its first snapshot OCR read; a screen whose
  content changes later is not judged again.
- How well the check finds a real failure is not measured; the sample
  had none confirmed.
- What a screen reader says is the tree's order, not the order a screen
  reader moved in on the recording.

## Step 23: a decorative image that is announced

### The owner's decision (2026-11-17)

Offered three AI checks to build next, the owner chose a decorative
image that a screen reader announces. Offered eight rules from the
measurement below, the owner chose: "Build rules 1 to 8 for the
decorative image check with low impact".

1. **One issue.** "Decorative image announced by a screen reader"
   (`decorative-image-announced`), WCAG 1.1.1.
2. **Which images.** An image with a description of its own that a
   screen reader says, the images the icon and description checks
   choose (step 21, rule 2), whether a screen reader stops on the image
   or says its description as part of the item that holds it.
3. **Names are not asked about.** An image that is a control, or whose
   description is all that the item holding it says, gives that control
   or item its name, so is not asked about: a control needs a name
   whatever its image shows. A poor name belongs to a separate check.
4. **The question.** The model for vision-based checks is given picture
   A, the whole screen with the image outlined, and picture B, a
   close-up, as for the other image checks, with what a screen reader
   says on the screen, the image's own description marked as coming
   from the image, so that it is not taken as other text that says the
   same. It answers informative, decorative or cannot-tell, reason
   first.
5. **Levels.** Decorative is a warning for a person to verify, low
   impact if confirmed. Cannot tell, or two answers that are still two
   after the model for complex questions, is a warning to check
   manually. Informative is no finding, and is counted.
6. **One answer for one image.** The answer is kept against the image's
   pixels, its description, what a screen reader says for it and what a
   screen reader says on the screen, and asked again of the model for
   complex questions where not settled.
7. **No model.** With no model for vision-based checks, nothing is
   judged, and the counts say how many images were not checked.
8. **Pictures out of step.** An image on a picture out of step with the
   tree is not asked about there, as for the other image checks.

### What the standards say (checked in cited documents)

- [WCAG 2.2's 1.1.1](https://www.w3.org/WAI/WCAG22/Understanding/non-text-content.html)
  asks that non-text content that is pure decoration be implemented so
  that assistive technology can ignore it. An image that only decorates,
  or only repeats what the text beside it says, and that a screen reader
  announces, does not.

### Measured first (2026-11-17)

A probe, with no model, read the 20 recordings with phone screens on
the development PC. The icon and description checks' rules chose 146
images with a description of their own, on pictures in step, on 17
recordings, and a screen reader says every one.

- **84 are controls,** spoken by the image itself: Settings' "Navigate
  up", previews, Chrome's toolbar, a mail app, the launcher and an app's
  library cards.
- **62 are spoken by an item that holds them.** For 51, the description
  is all the item says: 48 icons in the launcher's search bar (the
  Google app, AI Mode, voice search and camera search) and 3 Settings
  illustrations of the colour inversion animation. So rule 3 leaves
  them.
- **11 are said with other text:** the launcher weather widget's icons,
  "Cloudy" 10 times and "Flurries" once, said as "Cloudy, 7°". These are
  the only questions for the model in the sample.
- **No measure without a model.** No description was repeated at its
  own stop, in the stops before or after, or in text within 24 dp, and
  none was a generic word such as "image" or "icon". Text nearby matched
  unrelated app labels, such as an app's name above the search bar's
  icons, so it is not used.
- **Circularity.** In step 21's probe, Haiku answered the "Flurries"
  icon decorative twice, counting the widget's "Flurries, 3°", which
  includes the icon's own description, as other text; Sonnet gave
  decorative and informative. So the question marks the image's own
  description (rule 4).

### As built

- **The rules.** `PhoneDecorativeImageRun` (`Recorder.Session`) takes
  the questions of `PhoneDescribedImageRun`, the described images once
  each in a visit on pictures in step (rules 2 and 8), so it reads no
  picture again. `StateOf` compares letters and digits in lower case: a
  description no item a screen reader stops on says is not spoken
  (`NotSpoken`), for example where the holder has a description of its
  own; else an image that is a control is a control's name
  (`Controls`); an image spoken by its holder whose text is only the
  description is that item's name (`ItemNames`); any other is asked
  (`Questions`), said with other text by its holder or alone by the
  image itself.
- **The question.** `AndroidDecorativeImages` (`Recorder.Analysis`) asks
  `android-image-decorative`, version 1, of the project's model for
  vision-based checks, with the same pictures as the icon and
  description checks and a step key made from the request's SHA-256.
  The question gives the image's description and, where its holder says
  it, what the holder says, noting that the image's own words in it are
  the image's text alternative and not other text, and what a screen
  reader says on the screen with the image's place marked. It asks
  whether the image is informative (it gives information nothing else a
  screen reader says on the screen gives, leaving out its own
  description), decorative (it only decorates, or only repeats other
  text a screen reader says) or cannot-tell, reason first.
- **Rule 6, the signature.** As the icon and description checks': the
  image's pixels, its description, which item says it and what that
  item says, and what a screen reader says on the screen. An answer
  kept on a task of the project, by the same profile and model, is
  copied into the task (`SignedAsker`), and a question not settled is
  asked again of the model for complex questions.
- **Rule 5, the levels.** Agreed decorative: a warning, the model's
  inference, ending "a person should verify this". Agreed cannot-tell,
  or two answers: a warning ending "check manually". Agreed
  informative: no finding, counted (`Informative`). Every answer is the
  model's inference, so the report says AI was used even where no
  finding was made.
- **Rule 7.** Before any answer, and with no model for vision-based
  checks, no finding is made; the note says how many images were not
  judged and why.
- **Where it runs.** The Android report, the project report and the
  model checks ask after the screen title check; the model checks'
  Markdown has a section, "Decorative images", and the report's limits
  give the check's note. The Checks tab gives a row under each visit:
  the findings, or how many images were not said, were names of
  controls or of the items holding them, were judged informative or are
  not yet judged; and the summary gives the counts. "Ask the project's
  model" is enabled while an image is not judged.
- **Guidance.** `android-check-guidance.json` has the check, impact
  low, criterion 1.1.1, with remediation for Views, Compose, Flutter
  and React Native.

### Checked

The solution builds with no warnings, and the tests pass, 2,673 of
them, with the 10 that need Windows failing as before. The new tests
cover made-up items: a control's image, an image in a row with other
text, an image that is all its row says, an image a screen reader stops
on alone, and an image in a row with a description of its own; a
picture out of step; the letters-and-digits comparison; findings only
from warnings, with the rest counted; the requests' key, answers and
text, the image's own description marked; kept answers judging one
image informative and one decorative; no kept answers and no model;
and the levels of each answer and of two answers.

### Limits

- Only images in the tree with a description of their own are checked.
  A decorative image drawn as part of another view, or a description
  given only to its holder, is not.
- A control's image, and an image that names the item holding it, are
  not asked about, even where the image is decorative.
- No decorative image announced was in the measured sample, so how well
  the check finds one is not measured, and needs content that has one.
- What a screen reader says is the tree's order, not the order a screen
  reader moved in on the recording.

## Step 24: a heading that does not describe its section

### The owner's decision (2026-11-17)

Offered the heading check of WCAG 2.4.6 next, the owner chose it, and
asked that it be tied to the check of text shown as a heading but not
marked as one (step 19), so that such text is checked too. Asked about
the sections' content not on screen: "Wouldn't the content be in the
accessibility tree? You might warn that the result is only from the
tree, but you should be able to make some answer". After two more
probes, of items not on screen and of scrolling areas, the owner chose:
"Build the heading check with rules 1 to 10 as revised, with the
same-area and scrolling changes".

1. **One issue.** "Heading does not describe its section"
   (`heading-not-descriptive`), WCAG 2.4.6, beside "Text shown as a
   heading, not marked as one" (1.3.1). Text can have both, as their
   remedies differ.
2. **Which headings.** Items marked as headings that are shown, with
   text a screen reader says, and text not marked as a heading that the
   model judged a heading in step 19. Text the model judged not a
   heading, could not tell about, gave two answers for, or has not yet
   judged is counted, not checked. Each heading once a screen, from its
   first settled snapshot OCR read.
3. **The screen's title.** A heading that is the screen's title,
   letters and digits compared in lower case (in Chrome, the web page's
   title too), is left to the screen title check, counted, with no
   model asked.
4. **A section not on screen.** A heading with nothing under it, on
   screen, in the tree or in the text OCR read, within its scrolling
   area, is counted as having its section not on screen, not judged.
5. **The section.** The items after the heading in the tree's order up
   to the next heading, the next heading being one marked, on screen or
   not, or text shown as a heading not judged not to be one; only items
   in the heading's scrolling area, the nearest item holding the heading
   that is scrollable or can scroll, or in one inside it, or every item
   where the heading is in none. On screen: the items a screen reader
   stops on, not within the heading and not above it. Not on screen:
   the outermost items a screen reader would read. The text OCR read:
   the lines from the heading's bottom to the next heading below it, or
   else the bottom of the scrolling area. Each part is marked with where
   it came from, with the screen's title and the app's package.
6. **The picture.** Where images cover at least half of the section,
   the screen's picture is given, with the heading outlined, and the
   question is for the model for vision-based checks.
7. **Not on screen, and scrolling.** A finding whose section is partly
   from items not on screen says it was judged from the accessibility
   tree only. Where the section is the last on screen and its area can
   scroll down, the question says that it may continue below the
   screen.
8. **Levels.** A heading judged not to describe its section is a
   warning for a person to verify, medium impact if confirmed. Cannot
   tell, or two answers, is a warning to check manually. Describes is no
   finding, and is counted.
9. **The models.** The project's model for text-based checks, or for
   vision-based checks where pictured, asked twice, and asked again of
   the model for complex questions where not settled. With no model, a
   heading is not checked, and no finding is made.
10. **One answer for one heading.** A saved answer is reused only where
    the heading, all of its section's texts and, where given, the
    picture are unchanged.

### What the standards say (checked in cited documents)

- [WCAG 2.2's Understanding 2.4.6](https://www.w3.org/WAI/WCAG22/Understanding/headings-and-labels.html)
  asks that headings, where there are any, be descriptive, and says
  that they need not be long: "A word, or even a single character, may
  suffice if it provides an appropriate cue to finding and navigating
  content." It does not require headings, so a screen with none is not
  this issue. [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) applies it
  to software.

### Measured first (2026-11-17)

Three probes, with no model, read the 20 recordings with phone screens
on the development PC: 79 headings once a screen, 34 marked and 45 not
marked but found by step 19's rules.

- **Text shown as a heading.** All 45 unmarked headings were in one
  Flutter app, on 6 recordings. Step 19's kept answers judged 37 a
  heading and 5 not a heading (the word "OPEN"); 3 had two
  answers. So unmarked text is taken only where judged a heading.
- **The screen's title.** 13 headings were their screen's title, such
  as the titles in Settings' and TalkBack's collapsing bars, so are left
  to the screen title check.
- **Nothing under the heading.** 8 headings had nothing under them on
  screen.
- **Not on screen.** The companion records the whole tree, with each
  item's visibility. 6 sections had items not on screen, all with no
  bounds: 5 were real, such as a menu's "Options" over "Send feedback"
  and TalkBack's "Developer settings"; 1 was wrong, Chrome's toolbar
  taken by the last card on a page. 40 of the 55 snapshots with
  headings had speakable items not on screen somewhere.
- **Scrolling areas.** 64 of the 79 headings were in a scrolling area,
  51 of them in one that can scroll down; of 28 sections reaching the
  bottom of the screen, 16 could scroll further and 12 could not. 18
  sections took items outside their heading's area, such as Chrome's
  toolbar and an app's tab bar, so items are taken only from the
  heading's area. Keeping only the area's own items emptied 11, 10 of
  them titles in Settings' and TalkBack's collapsing bar, whose content
  is in a list inside the area; one heading with no area had a calendar
  of 33 items in a scrolling area of its own. So items in an area
  inside the heading's are kept, and a heading in no area keeps every
  item.
- **No measure without a model.** Generic headings, such as "Options"
  four times and "Others", could not be told apart from good ones by
  words shared with their sections, so every heading the rules leave is
  asked.

### As built

- **The rules.** `PhoneHeadingSectionRun` (`Recorder.Session`) reads
  each settled snapshot OCR read (`SnapshotsRead`). The headings shown
  are items marked as headings, visible, with bounds and speakable
  text, and items step 19 made a finding of on the visit, by their
  identity, with `VerdictOf` its finding: no inference, not judged; an
  error, a heading; information, not a heading; else unsettled. Each is
  taken once a screen, by whether it is marked and its letters and
  digits (`Entries`, with `Marked`, `Unmarked`, `NotJudgedHeadings`,
  `ScreenTitles` and `EmptySections`). The title is
  `PhoneScreenChecks.ScreenTitle`'s, and in Chrome also
  `PhoneScreenTitleRun.PageTitle`'s.
- **Rule 5.** `ScrollingArea` is the nearest item holding the heading
  that is scrollable or has ACTION_SCROLL_FORWARD, ACTION_SCROLL_BACKWARD
  or the scroll up or down actions. The section's items on screen are
  those `PhoneTreeChecks.ShouldFocusView` stops on with speakable text
  whose bottom is below the heading's top; those not on screen are
  important for accessibility, focusable, clickable, long clickable,
  focusable by a screen reader or with text or a description of their
  own, with speakable text, and not within one already taken. The lines
  OCR read start no more than 2 pixels above the heading's bottom. Each
  text is joined, items by " | " and lines by " / ", and cut to 4,000
  characters from its start.
- **Rule 6.** The share of the section, from the heading's bottom to
  the next heading below it or the area's bottom, across the screen,
  that visible ImageViews cover, on a 60 by 60 grid
  (`PhoneScreenTitleRun.ImageShare`); from 0.5 the question is pictured
  (`PicturedQuestions`), else text only (`TextQuestions`).
- **Rule 7.** `CanScrollDown` is ACTION_SCROLL_FORWARD or the scroll
  down action on the heading's area, for the last section on screen.
  What was measured, in the finding, gives the count of items on
  screen and lines, that part of the section was judged from the
  accessibility tree only, with how many items are not on screen, and
  that it may continue below the screen.
- **Linked to step 19.** Until step 19's answers are given, no unmarked
  text is a heading. `WithHeadingLook` runs the rules again on the same
  measurement; the Android report, the project report and the model
  checks run it after step 19's questions are asked, and the Checks tab
  after step 19's kept answers are given.
- **The questions.** `AndroidHeadingSections` (`Recorder.Analysis`)
  asks `android-heading-section`, with no picture, of the project's
  model for text-based checks, and `android-heading-section-pictured`,
  with picture A, the whole screen at half size with the heading
  outlined, of its model for vision-based checks, each version 1, with
  step keys made from the request's SHA-256. The question gives the
  app's package, the screen's title, the heading and whether it is
  marked or only shown as a heading, what a screen reader says in the
  section on screen, the section's items not on screen, which a screen
  reader reaches by scrolling, the text OCR read in it, noting that OCR
  may misread, and where the section may continue below the screen. It
  asks whether the heading describes its section: describes-the-section,
  does-not-describe (such as a word that would fit any section, a
  heading of a different topic than its section's, or text that names
  nothing the section holds) or cannot-tell, reason first.
- **Rule 10, the signature.** As the screen title check's: for a
  pictured question, the whole screen's pixels; for a text question,
  the texts only (`AndroidImageAlternative.TextSignature`). The text is
  the app, the screen's title, whether the heading is marked, the
  heading, the items not on screen, the OCR text and whether it may
  continue, with what a screen reader says in the section. An answer
  kept on a task of the project, by the same profile and model, is
  copied into the task (`SignedAsker`).
- **Rule 8, the levels.** Agreed does-not-describe: a warning, the
  model's inference, ending "a person should verify this". Agreed
  cannot-tell, or two answers: a warning ending "check manually".
  Agreed describes-the-section: no finding, counted (`Described`).
  Every answer is the model's inference, so the report says AI was used
  even where no finding was made.
- **Rule 9.** Before any answer, and with no model for a question's
  kind, no finding is made; the note says how many were not judged and
  why, for text-based and vision-based checks apart.
- **Where it runs.** The Android report, the project report and the
  model checks ask after the decorative image check; the model checks'
  Markdown has a section, "Headings and sections", and the report's
  limits give the check's note. The Checks tab gives a row under each
  visit: the findings, or how many headings first seen on it were
  judged to describe their sections, are not yet judged, are the
  screen's title, have nothing under them, or are text not judged a
  heading; and the summary gives the counts. "Ask the project's model"
  is enabled while a heading is not judged and the project can use the
  model for its kind.
- **Guidance.** `android-check-guidance.json` has the check, impact
  medium, criterion 2.4.6, related 1.3.1, with remediation for Views,
  Compose, Flutter and React Native.

### Checked

The solution builds with no warnings, and the tests pass, 2,680 of
them, with the 10 that need Windows failing as before. The new tests
cover a made-up screen: a heading that is the window's title; a
section in a scrolling list, leaving out a line drawn above its heading
and a tab bar outside the list; an item not on screen, with the
finding's words; a last section that may continue; with no list, the
tab bar in the section; no OCR; the picture under and over half the
section; text shown as a heading taken only once judged one, and text
judged not a heading not ending a section; the requests' use, template,
picture, text and signatures; kept answers judging a warning and a
heading that describes its section; no model; and the levels of each
answer and of two answers.

### Limits

- The tree's order is taken as the reading order, and the next heading
  in it as the section's end; a layout drawn in another order may give
  a section the wrong items.
- Items not on screen have no bounds, so are taken by the tree's order
  only; on one Chrome page the last card took Chrome's toolbar.
- Each heading is judged on its first snapshot OCR read; a section that
  changes later is not judged again.
- How well the check finds a real failure is not measured; the sample
  had none confirmed.

## Step 25: text on screen not in the tree

### The owner's decision (2026-11-18)

Offered eight rules from the first probe below, with two versions of
the rule for pictures out of step, the owner asked to test the second,
then to add a rule for a tree not yet complete, and chose: "Build the
check with rules 1 to 10, including rule 2c, at high impact".

1. **Which lines.** The lines OCR read on a settled snapshot's picture
   that no item's text holds, with at least 3 letters or digits, each
   once a visit, from the first snapshot in step with the tree it is
   seen on.
2. **Pictures out of step.** (2b) A picture is out of step with the tree
   where the tree has at least 3 text items, not images, and OCR found
   fewer than half of them where the tree places them; its lines are
   counted, not judged. (2c) After rule 7, a line that an item shown in
   another snapshot of the same visit holds is set aside, as the tree
   was not complete when this snapshot was taken.
3. **TalkBack's caption** is left out, as for the image of text check.
4. **Images.** A line inside an image, an ImageView or a web image
   (`android.widget.Image`), is left to the image of text check.
5. **Held elsewhere.** A line a visible item elsewhere on the screen
   holds is in the tree, so is counted, not reported.
6. **Held by an item not shown.** A line an item marked not shown holds
   is still asked about, and the question says so.
7. **Misreads.** A line inside an item, not a WebView, that covers at
   most a quarter of the screen and has its own text, hint or
   description is taken as that text misread. A wrong label there is the
   label in name check's (2.5.3).
8. **The question.** The rest are asked of the project's model for
   vision-based checks, with the whole screen with the line outlined, a
   close-up, the line as OCR read it, the item it is drawn in and what a
   screen reader says on the screen. It answers drawn text a screen
   reader cannot reach, text behind a dialog or scrim or covered, not
   text (a shape or icon read as letters), or cannot tell, reason first.
9. **Levels.** Drawn text is a warning for a person to verify, high
   impact if confirmed (1.1.1, 4.1.2). Cannot tell, or two answers, is
   a warning to check manually. The other answers are counted. With no
   model, nothing is judged.
10. **One finding a line.** Each line is one finding in each visit, its
    answer kept and reused by signature as for the other checks.

### What the standards say (checked in cited documents)

- [WCAG 2.2's Understanding 1.1.1](https://www.w3.org/WAI/WCAG22/Understanding/non-text-content.html)
  defines non-text content as "any content that is not a sequence of
  characters that can be programmatically determined", and asks that it
  have a text alternative. Text drawn as pixels, with no item a screen
  reader can reach, cannot be programmatically determined, so is
  non-text content with no alternative.
- [WCAG 2.2's Understanding 4.1.2](https://www.w3.org/WAI/WCAG22/Understanding/name-role-value.html)
  asks that the name of every user interface component can be
  programmatically determined, and that custom controls take
  "additional measures" to give assistive technologies that
  information; a custom view that draws its text and gives no name does
  not. [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) applies both to
  software.

### Measured first (2026-11-18)

A probe, with no model, read the 20 recordings with phone screens on
the development PC: 140 snapshots read with OCR held 734 lines in no
item's text, 493 once a screen.

- **Out of step, the first version (2a).** The image checks' rule, more
  stray lines than tree text found, took 26 snapshots and 411 lines. Of
  6 pictures opened, 5 showed another screen or a transition; the sixth
  a dialog over a page. That rule would never check an app that draws
  most of its text, as its pictures would look out of step.
- **Set aside by the other rules.** 25 lines were TalkBack's caption,
  11 inside an image, 1 short, and 15 held by a visible item elsewhere,
  such as a scrolled mail list. None was held only by an item not
  shown.
- **Of the 30 left,** 19 were OCR misreads of a small item's own text,
  mostly launcher labels and the date widget on video frames; 2 were in
  a web image; 5 text behind a bottom sheet's scrim and 2 behind a web
  dialog; and 2 page dots read as letters.
- **Rule 2b.** At least 3 text items and fewer than half found where the
  tree places them took 44 snapshots. They agreed with 2a on 25. The 19
  only 2b took were Home screen frames, blurred or with labels OCR
  could not read over the wallpaper. The 1 only 2a took was a snapshot
  whose tree had 4 text items where the visit's other snapshots had 9:
  2b sent its 10 lines on.
- **Rule 2c.** After 2b and rules 3 to 7, an item shown in another
  snapshot of the visit held 15 lines: the 10 above, and 5 behind the
  bottom sheet, which the visit's other snapshots hold. Each was checked
  against the item that held it.
- **What each gives the model.**

| Rule 2 | Lines for the model | Screens |
|---|---|---|
| 2a | 10 | 5 |
| 2b | 19 | 5 |
| 2b, then 2c | 4 | 3 |

- **The 4 left:** 2 lines behind a web dialog that none of the visit's 6
  snapshots holds, 1 garbled app bar title on a visit with one
  snapshot, and 1 set of page dots.
- **Limits of the measure.** 39 of the 65 visits had one snapshot, where
  2c has nothing to compare. No recording had text truly drawn where a
  screen reader cannot reach it, so how well the check finds it is not
  measured.

### As built

- **The rules.** `PhoneTextNotInTreeRun` (`Recorder.Session`) reads each
  settled snapshot with a tree that OCR read (`SnapshotsRead`). Its
  lines found by OCR in no item's text are taken; a snapshot `OutOfStep`
  (rule 2b) is counted (`OutOfStepSnapshots`, `OutOfStepLines`) and
  skipped. Each line is taken once a visit by its letters and digits, as
  `PhoneTextMatching.Spelling` gives them, and sorted in this order:
  TalkBack's caption, as the image of text check finds it
  (`PhoneImagesOfTextRun.CaptionSaid`, `Captions`); its centre inside a
  visible ImageView or `android.widget.Image` (`InImages`); fewer than 3
  letters or digits (`ShortLines`); held by the visible items
  (`HeldElsewhere`); inside a small item with words of its own, not a
  WebView (`Misreads`); held by the visible items of another snapshot of
  the visit (`OtherSnapshots`); else asked (`Questions`). Held is as the
  image checks match words: every word of two or more letters among the
  items' words, a word of four or more within one letter of one, or the
  line's spelling within the items' spellings run together.
- **Rule 6.** Whether the items not shown hold the line, the same way,
  is given to the question (`HeldByHidden`) and to what was measured.
- **The question.** `AndroidTextNotInTree` (`Recorder.Analysis`) asks
  `android-text-not-in-tree`, version 1, of the project's model for
  vision-based checks, with picture A, the whole screen with the line
  outlined, and picture B, a close-up, as the image checks give them,
  and a step key made from the request's SHA-256. The question gives
  the app's package, the screen's name, the line as OCR read it, noting
  that OCR may misread, that no item a screen reader is given holds it,
  the class of the item it is drawn in and that item's own words, where
  an item not shown holds it, and what a screen reader says on the
  screen, item by item, cut to 4,000 characters. It answers drawn-text,
  covered, not-text (which includes a garbled reading of text a screen
  reader says) or cannot-tell, reason first.
- **Rule 10, the signature.** The line's pixels, the app, the line, the
  item's class and words, whether an item not shown holds it, and what
  a screen reader says on the screen. An answer kept on a task of the
  project, by the same profile and model, is copied into the task
  (`SignedAsker`), and a question not settled is asked again of the
  model for complex questions.
- **Rule 9, the levels.** Agreed drawn-text: a warning, the model's
  inference, ending "a person should verify this". Agreed cannot-tell,
  or two answers: a warning ending "check manually". Agreed covered or
  not-text: no finding, counted (`Covered`, `NotText`). Every answer is
  the model's inference, so the report says AI was used even where no
  finding was made. Before any answer, and with no model for
  vision-based checks, no finding is made; the note says how many lines
  were not judged and why.
- **Where it runs.** The Android report, the project report and the
  model checks ask after the heading section check; the model checks'
  Markdown has a section, "Text not in the tree", and the report's
  limits give the check's note. The Checks tab gives a row under each
  visit: the findings, or what became of the visit's lines; and the
  summary gives the counts. "Ask the project's model" is enabled while
  a line is not judged.
- **Guidance.** `android-check-guidance.json` has the check,
  `text-not-in-tree`, impact high, criteria 1.1.1 and 4.1.2, related
  1.3.1, with remediation for Views, Compose, Flutter and React Native.

### Checked

The solution builds with no warnings, and the tests pass, 2,688 of
them, with the 10 that need Windows failing as before. The new tests
cover a made-up screen: a line held by a visible item elsewhere, two
lines drawn in a custom view with no text of its own, one of them held
by an item not shown, a line in another snapshot's tree, a short line,
a line inside an image and a line inside a small button with its own
text; each line once a visit; no OCR; a picture out of step and one in
step; the request's key, use, pictures, answers and text; kept answers
judging one line drawn text and one covered, and one not text; no
model; and the levels of each answer and of two answers.

### Limits

- On a visit with one snapshot, rule 2c has nothing to compare, so a
  tree not yet complete reaches the model.
- Held is matched by words, not by one item, so a line whose words are
  all said in different items is taken as held.
- A line inside a small item with words of its own is taken as a
  misread, even where the item also draws other text.
- How well the check finds drawn text is not measured; the sample had
  none.

## Step 26: web images in the images-of-text check

### The owner's decision (2026-11-18)

Asked why web images were not tested as images of text, and told it was
a gap, the owner asked to "Extend the images-of-text check to count web
images and measure it on my recordings first". Offered five rules from
the measurement below, the owner chose: "yes, update the images of text
to handle web images using the 5 proposed rules and handle the alt text
issue". The alt text issue is an image that has not loaded, whose alt
text the browser draws in its place: rule 3 was first offered as no
rule, and is built as a question to the model, below.

### Measured on the development PC's recordings (2026-11-18)

A probe, with no model, ran the images-of-text rules on the 20
recordings on the PC, with and without web images, and was deleted
after.

- **The probe's copy of the rules** gave the recorder's findings on all
  20 recordings: 17 findings and 5 not judged, from 145 snapshots OCR
  read.
- **Web images** (`android.widget.Image`, the class a WebView or a
  browser gives an `img`): 9 on all visits, seen 96 times, all inside a
  WebView, in a browser and an email app. OCR read no words in 6.
- **The 3 with words:** a picture of three phone screens, a finding
  with 2 misread lines kept after 3 lines held beside it were set aside,
  a picture with significant other visual content for the model to
  judge; a logo, not judged as its picture was out of step with the
  tree; and an email's image that had not loaded, whose alt text the
  browser drew in the image's box, passed only because TalkBack's
  caption rule set the line aside.
- **Alt text** was in the image's description in all 3, never in its
  text.
- **No knock-on effect:** counting web images changed the outcome of no
  ImageView, no line inside a web image was held by another item's
  text, and no other class named as an image was seen.

### As built

1. **A web image is an image.** A visible `android.widget.Image` with an
   area counts as an image everywhere the images-of-text check uses
   images: the lines inside it, the items beside it (an image is not an
   item beside another), the app's own text (a line an image holds is
   not the app's text) and the count of a picture out of step with the
   tree. The four rules before it are unchanged
   (`PhoneTextMatching.IsImage`). The checks that borrow the
   out-of-step count, of an image with no text alternative (step 20)
   and of an icon and its description (step 21), still count
   ImageViews only.
2. **Alt text from the text as well.** A browser may give an image's
   alt text as its text or its description, so a web image's alt text
   is both, the same words once (`PhoneImagesOfTextRun.AltText`). It is
   what "its alt text holds them" or "it has no alt text" is measured
   against, what Flutter's rule of a described image holding the words
   reads, and what the model is given.
3. **An image that has not loaded.** No rule without a model can tell a
   browser's stand-in for an image from an image of text, so the
   model's question for a web image says that the image is on a web
   page, and that a browser that has not loaded an image draws a
   stand-in in its place, such as its alt text or a broken-image icon;
   it names the image's alt text as such, and adds a sixth answer,
   `not-loaded`. Both answers agreeing that it had not loaded makes the
   finding "not run", as the image itself was not seen, with the
   model's reason and "check with the image loaded"; at AAA the 1.4.9
   finding is the same. Answers that differ are a warning, as before.
   An ImageView's question, its five answers and its key are as they
   were, so answers kept for ImageViews still hold; the template stays
   version 2.
4. **Web remediation.** A finding on a web image, or on an image inside
   a WebView, is marked as web content (`PhoneFinding.Web`), and the
   report gives it "Suggested remediation for web content:" in place of
   Android's, with no Flutter or React Native remediation, and the web's
   sources (WCAG techniques C22 and H37) in place of Android's. At
   1.4.5: "Show the words as HTML text styled with CSS instead of an
   image. Keep images of text only for logos or where the look is
   essential, and give the img element alt text with the same words."
   The impact stays medium at 1.4.5 and low at 1.4.9. The guidance file
   is version 4 ([android-report.md](android-report.md)).
5. **The check of text not in the tree** (step 25) needs no change: it
   already leaves a line inside a web image to this check, which now
   reads it, so the gap is gone from its limits.

### Checked

The solution builds with no warnings, and the tests pass, 2,698 of
them, with the tests that need Windows failing as before. The new tests
cover a web image inside a WebView beside an ImageView, each a finding
and only the web image's marked as web content; a web image's alt text
from its text, its description, both, the same words in both, and
neither; the web image's question and answers and the ImageView's
unchanged; two answers that it had not loaded making the finding not
run; and the report's web remediation and sources for a web image, and
Android's for an ImageView.

### Limits

- The sample had 3 web images with words, so how often a web image of
  text is found, and how well the model tells an image that had not
  loaded, is not measured.
- A finding is web content only by its image's class or a WebView above
  it; a web page drawn in a view of another class is not known as one.
- Only the images-of-text checks count web images and give web
  remediation; the other image checks (steps 20, 21 and 23) still look
  only at ImageViews.

## Step 27: a heading that does not look like one

### The owner's decision (2026-11-18)

Offered nine rules from the first probe below, with two options for
rules 4 and 5, the owner chose "Option A for rules 4 and 5, and measure
the model's answers on the 59 headings first". After the first run with
the model, two changes were proposed, to rules 3 and 7; the owner asked
to "Run the probe once more to measure both rule changes on these
headings", then chose: "Build the check with these two rules and the
Android checks document updates".

1. **The issue.** "Marked as a heading but not shown as one (WCAG
   1.3.1)", its own issue, apart from text shown as a heading but not
   marked (step 19), as the fix is the opposite.
2. **Which headings.** Each item marked as a heading, or text inside
   one, that OCR placed on a settled snapshot's picture, once a visit,
   from the first such snapshot. A heading OCR placed on no picture of
   its visit is counted, with no finding.
3. **Placement.** A heading is left out, and counted, where (3a) OCR's
   text differs from the app's, (3b) another line covers at least half
   of its box, or (3c) another line in the same row is within half the
   heading's height of its box, as OCR's line goes on past it. 3c is
   the first of the two changes.
4. **No pass without a model** (option A). Every heading left goes to
   the model, as large text, such as an amount, can still be a value.
5. **Text in something that can be tapped** (option A) is included, as
   a heading can also be a link.
6. **The question.** Step 19's question, pictures and three answers,
   heading, not-heading or cannot-tell, reason first, with the question
   saying that the text is marked as a heading for a screen reader.
7. **Levels.** Asked twice of the project's model for vision-based
   checks. Answers that do not settle the question are put to the
   model for complex questions, as for the other checks. Answers that
   agree the text is not a heading are put to it too, to confirm (the
   second change); its answers decide. Agreed not a heading: a warning
   for a person to verify, with the model's reason. Agreed a heading:
   no finding, counted. Cannot tell, or two answers: a warning to check
   manually. Where the project has no model for complex questions, or
   the same model for both, the first answers stand.
8. **No model.** With no model for vision-based checks, the headings
   are counted as not judged, with no finding.
9. **Kept answers.** An answer is kept by its request, so it is given
   again only where the text and the pictures are unchanged.

### What the standards say (checked in cited documents)

- [WCAG 2.2's failure F43](https://www.w3.org/WAI/WCAG22/Techniques/failures/F43),
  "Failure of Success Criterion 1.3.1 due to using structural markup in
  a way that does not represent relationships in the content", gives as
  its first example a heading used only for visual effect: an address
  marked as a heading to show it in a large bold font. Text marked as a
  heading that heads no section, such as a value or a field's label,
  is that failure.
- [WCAG 2.2's Understanding 1.3.1](https://www.w3.org/WAI/WCAG22/Understanding/info-and-relationships.html)
  asks that information and relationships conveyed through presentation
  can be programmatically determined; a heading mark tells a screen
  reader of a relationship, a section, that the screen does not show.
  [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) applies it to software.
- **Impact.** Medium is an assumption, not taken from a source, chosen
  to match the check of text shown as a heading but not marked.

### Measured first (2026-11-18)

**With no model.** A probe read the 20 recordings with phone screens on
the development PC. OCR placed 59 marked headings, once a visit, on 9
of them, 31 different texts; 68 more were never placed, mostly items
scrolled off the screen. By app: Settings 41, Chrome 9, TalkBack 6,
Gmail 2 and the launcher 1. 57 were measured on video frames, so their
colours are approximate.

| Group | Count | What was measured |
|---|---|---|
| Screen titles at the top | 21 | Letters 1.18 to 2.03 times the body text's height |
| Section labels in Settings and TalkBack lists | 27 | Letters 0.72 to 1.16 times the body text's height, 22 the body text's grey |
| Chrome web pages | 9 | All inside an item that can be tapped |
| Gmail | 2 | Column headers in capitals, 0.59 times the body text's height |

16 had letters at least 1.35 times the body text's height, 2 were in
capitals, 28 differed in colour from the body text, and 25 had none of
these, so size, case and colour cannot tell whether a marked heading is
shown as one; hence option A, no pass without a model.

**Run 1, with the model (old rule 3, unsettled answers to the model for
complex questions).** Claude Haiku as the model for vision-based checks
and Claude Sonnet as the model for complex questions. 55 headings were
asked; 4 were left out: "Date", where OCR read "Payments"; "Developer
settings" and "Advanced settings", where OCR read "settings"; and "All
apps", covered by "Home". Haiku settled 39 of the 55; 16 went to
Sonnet, which agreed with itself on all 16. The final answers were
heading 49 and not-heading 6. 4 of the 6 were real problems: a balance
amount on a web page, a value (F43); a field's label on the same page;
and two column headers of a table in an email. 2 were wrong: "Display",
placed by OCR on the first word of "Display size & text", and
"Braille", a section label. The run cost US$0.22.

**Run 2, with both changes.** Rule 3c left out only "Display", and no
heading placed correctly; 5 were left out and 54 asked. Haiku agreed
not a heading on 6, all put to Sonnet: it confirmed 2 (the amount and the
field's label), overturned 3 ("Tutorial & help", "Screen reader"
and "Options", all section labels) and split on 1 (one of the column
headers, cannot tell). 12 questions Haiku did not settle went to Sonnet too; its
two answers agreed on 17 of its 18 questions. The final answers were
not-heading 3 (the amount, the field's label and the other column
header), cannot tell 1 and heading 50, with no wrong not-heading answer. The run cost
US$0.24, Haiku US$0.033 and Sonnet US$0.208, about US$0.0045 a heading.
Between the runs, the final answers differed on 3 headings: "Braille",
as Haiku's answer changed; "Display", left out by rule 3c; and
the first column header, as Sonnet split.

### As built

- **The rules.** `PhoneHeadingNotShownRun` (`Recorder.Session`) reads
  each settled snapshot whose picture OCR read (`SnapshotsRead`). Each
  item marked as a heading, or text inside one (`HeadingOf`), placed by
  OCR, is taken once a visit by its text, from the first snapshot it is
  placed on (`Entries`); one placed on no picture of its visit is
  counted (`NotPlaced`). Rule 3 sets aside, in this order: OCR's text
  not the app's (`OcrDiffers`), compared by letters and digits, lower
  case, "o" as "0" and "l" and "i" as "1", within a fifth of the app's
  length in edits (`SameText`); another line covering at least half the
  box (`Covered`, `MostCovered` 0.5); and another line in the same row,
  overlapping by at least half the shorter height, within half the
  heading's height of its box (`LineGoesOn`, `LineGap` 0.5). The rest
  are asked (`Questions`), as step 19's `PhoneHeadingQuestion`.
- **The question.** `AndroidHeadingNotShown` (`Recorder.Analysis`) asks
  `android-heading-not-shown`, version 1, of the project's model for
  vision-based checks, with step 19's instructions, answers and
  pictures: picture A, the whole screen with the text outlined, and
  picture B, the screen from the text down at full size. The question
  is `AndroidHeadingLook.Question` with the text "marked as a heading
  for a screen reader". The step key is `heading-not-shown-` and 16 hex
  digits of the SHA-256 of the template, its version, the instructions,
  the question and both pictures, so an answer is reused only for the
  same text and pictures (rule 9).
- **Rule 7.** `AskQuestionAsync` asks runs 1 and 2 of the model for
  vision-based checks, then runs 3 and 4 of the model for complex
  questions, where there is another, when `AskAgain` holds: the first
  answers do not settle the question, or agree it is not a heading.
  Where the question put to the model for complex questions fails, the
  first answers stand. `Judge` takes the deciding answers: agreed a
  heading is counted (`Shown`), information, not a finding; any other
  is a warning on the heading's visit, labelled `heading '…'`, at the
  heading item's bounds. Agreed not a heading ends "a person should
  verify this"; cannot tell, or two answers, ends "check manually". The
  confirmed answers say that the model for simple questions answered
  not-heading and the model for complex questions was asked to
  confirm; the note counts the headings asked again "to settle or to
  confirm the first answers".
- **Rule 8.** With no model for vision-based checks, or before any
  answer, no finding is made; the note says how many headings were not
  judged and why. Every answer is the model's inference, so the report
  says AI was used even where every heading was judged shown.
- **Where it runs.** The Android report, the project report and the
  model checks ask after the check of text not in the tree; the model
  checks' Markdown has a section, "Marked headings not shown as
  headings", and the report's limits give the check's note. The Checks
  tab gives a row under each visit, the findings or what became of the
  visit's headings, from the kept answers (`FromKept`); the summary has
  a section with the counts placed, left out, not placed, for the
  model, judged shown and not judged. The toast and "Ask the project's
  model" count the headings not judged.
- **Guidance.** `android-check-guidance.json` has the check,
  `heading-not-shown`, impact medium, criteria 1.3.1, citing F43, with
  remediation for Views and Compose, Flutter, React Native and the web.

### Checked

The solution builds with no warnings, and the tests pass, 2,722 of
them, with the 10 that need Windows failing as before. The new tests
cover a made-up screen: a heading taken once a visit, text inside a
marked layout, a heading OCR read as other text, one covered by another
line, one whose line goes on past it, and one never placed; what OCR
may misread and still be the app's text; the request's key, use,
pictures and question; kept answers confirmed not a heading, overturned
as a heading, split, waiting for the model for complex questions, with
no model for complex questions, and with no answers; the level of each
agreed answer; and, in the analysis worker, the model for complex
questions asked to confirm agreed not a heading, not asked where the
first answers agree a heading, and the first answers standing with no
model for complex questions.

### Limits

- Misses, where the model says heading for text that is not shown as
  one, are not measured; the sample had only 4 real problems.
- Rule 3c is an approximation: OCR's lines are not kept as lines, so
  another line in the same row is taken as OCR's line going on. It was
  measured on these 59 headings only.
- The question's saying the text is marked showed in Haiku's reasons,
  but did not decide its answers on this sample.
- A heading OCR never placed, such as one scrolled off the screen, is
  not judged.
- The impact, medium, is an assumption (see above).

## Step 28: a control name that does not say what the control does

### The owner's decision (2026-11-18)

The owner asked for this check as the one left that the recordings to
hand can test: "okay, let's do that: A control name that doesn't say
what the control does". After the probe below, seven rules were
offered, with options for rules 2 to 5 and 7, and the owner chose:
"Build rules 1, 2A, 3A, 4A, 5A, 6 and 7A as proposed".

1. **Which controls.** Named controls a screen reader stops on only:
   visible, not a field, with bounds, that can be tapped, long pressed
   or checked. Unnamed controls stay with the existing missing-name
   check.
2. **The criterion (2A).** WCAG 1.1.1 for a control that shows no text,
   whose name is its text alternative; WCAG 2.4.6 for a control that
   shows text, whose name is its label. One check, "Control name that
   does not say what the control does (WCAG 1.1.1, 2.4.6)", with the
   criterion that applies given in each finding's measured text.
3. **Placeholders (3A).** A name that is a generic word, a resource
   name, a numbered name or the control's class name is a warning with
   no model.
4. **How often (4A).** Each other name is asked once for each screen,
   and the answer is given to every visit of the screen with the name.
5. **Overlap with the icon check (5A).** Controls the icon check also
   asks about are kept, as that check asks whether the icon fits the
   name, not whether the name says what the control does.
6. **The question.** Picture A, the whole screen with the control
   outlined; picture B, a close-up. The question gives the name, class
   and state, the text OCR read inside the control, and what a screen
   reader says on the screen, with three answers, reason first:
   describes, does-not-describe or cannot-tell.
7. **Levels (7A).** Asked twice of the project's model for vision-based
   checks. Agreed does-not-describe is put to the model for complex
   questions to confirm, as are answers that do not settle the
   question; its answers decide. Agreed does-not-describe: a warning
   for a person to verify. Cannot tell, or two answers: a warning to
   check manually. Agreed describes: counted, no finding. With no
   model, only the placeholder warnings are made.

### What the standards say (checked in cited documents)

- [WCAG 2.2's Understanding 1.1.1](https://www.w3.org/WAI/WCAG22/Understanding/non-text-content.html)
  says that a control's text alternative is to have "a name that
  describes its purpose". An icon-only control's name is its text
  alternative.
- [WCAG 2.2's Understanding 2.4.6](https://www.w3.org/WAI/WCAG22/Understanding/headings-and-labels.html)
  says that "if labels are present, they must be accurate and
  sufficiently clear or descriptive". A control that shows text has a
  visible label. [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) applies
  both to software.
- **Impact.** Medium is an assumption, not taken from a source, chosen
  to match the other checks of names and headings judged by a model.

### Measured first (2026-11-18)

A probe with no model read the 20 recordings with phone screens on the
development PC; the probe and its copies were deleted afterwards.

- **Controls.** 985 named controls, once a visit, of which 945 are
  stops for a screen reader, with 406 different names for each screen
  and 350 for each app. 40 are not stops, mostly switches read by
  their row; 81 more controls had no name.
- **By app, of the stops.** The launcher 405, the client's app 225,
  Settings 197, Gmail 59, Chrome 31 and TalkBack 28. No web content
  was found as controls. 895 were measured on video frames and 50 on
  screenshots.
- **Rule 3's lists.** No name was a generic word, a resource name, a
  numbered name or a class name, so rule 3 is tested only by unit
  tests. 67 names had no letters.
- **The icon check.** 89 of the stops are also asked about by the icon
  check (rule 5).
- **OCR.** Matching a control's name to the text OCR read inside it was
  unreliable on video frames, so the text is given to the model, not
  used to decide.
- **Looked at in the pictures (inferred, the assistant's own
  judgement).** Three kinds of name looked as if they did not say what
  the control does: an icon button named only by a count on its badge,
  day cells of a calendar named only by their number, and a barrier
  behind a dialog named with a developer's word. The others looked
  descriptive.

The model's answers were not measured before building, as the owner
asked for the build.

### As built

- **The rules.** `PhoneControlNameRun` (`Recorder.Session`) reads each
  settled snapshot whose picture OCR read (`SnapshotsRead`). Each item
  that `IsControl` holds for (visible, not editable, with bounds, and
  clickable, long clickable or checkable), that a screen reader stops
  on (`ShouldFocusView`), with a name (`SpeakableText`), is taken once a
  visit (`Controls`). Names are kept once a visit, in lower case
  (`Entries`), with every time a control with the name was seen.
- **Rule 2.** A control shows text where the tree gives text for it or
  a visible item inside it, or OCR read a line whose middle is inside
  its bounds (`ShownText`, at most 300 characters). Its measured text
  says "it shows text, so WCAG 2.4.6 applies to its label" or "neither
  the tree nor OCR gives it text, so WCAG 1.1.1 applies if it shows
  none".
- **Rule 3.** `Placeholder` gives "a generic word" for a whole name in
  the generic list, such as "button", "icon" or "click here", compared
  in lower case with punctuation as spaces; "its class name"; "a
  numbered name", such as "Button 2"; or "a resource name": one with
  ":id/", one in snake case, or one in camel case with a word that
  names a view, such as "btn", "icon" or "menu", so that a product
  name in camel case is not taken as one. These are warnings, with no
  model (`PlaceholderFindings`).
- **Rule 4.** Every other name is asked once for each screen
  (`Questions`), from the first time it was seen; `Judged` gives each
  answer to every visit of the screen with the name.
- **The question.** `AndroidControlNames` (`Recorder.Analysis`) asks
  `android-control-name`, version 1, of the project's model for
  vision-based checks: picture A, the whole screen with the control
  outlined in cyan, and picture B, the icon check's close-up; the name,
  class and state; OCR's text inside it, or that OCR read none; and
  what a screen reader says on the screen, with the control's place
  marked. The step key is `control-name-` and 16 hex digits of the
  SHA-256 of the template, its version, the instructions, the question
  and both pictures, so an answer is reused only for the same name,
  text and pictures. A question whose picture cannot be read, or whose
  control is not on it, is not asked.
- **Rule 7.** As for step 27: `AskQuestionAsync` asks runs 1 and 2,
  then runs 3 and 4 of the model for complex questions, where there is
  another, when `AskAgain` holds: the first answers do not settle the
  question, or agree does-not-describe. `Judge` takes the deciding
  answers: agreed describes is counted (`Described`), information, not
  a finding; any other is a warning on every visit with the name.
  Agreed does-not-describe ends "a person should verify this"; cannot
  tell, or two answers, ends "check manually".
- **Where it runs.** The Android report, the project report and the
  model checks ask after the check of a marked heading not shown as
  one; the model checks' Markdown has a section, "Control names that do
  not say what the control does", and the report's limits give the
  check's note. The Checks tab gives a row under each visit, from the
  kept answers (`FromKept`); the summary has a section with the counts;
  the toast and "Ask the project's model" count the names not judged.
  The check runs at both audit levels, as 1.1.1 is level A.
- **Guidance.** `android-check-guidance.json` has the check,
  `control-name-not-descriptive`, impact medium, criteria 1.1.1 and
  2.4.6, related 4.1.2 and 2.5.3, with remediation for Views and
  Compose, Flutter, React Native and the web.

### Checked

The solution builds with no warnings, and the tests pass, 2,745 of
them, with the tests that need Windows failing as before. The new
tests use a made-up screen: an icon button, a button with text OCR
read, a resource name and a generic word, which are warnings with no
model, an image that cannot be tapped, a field, an unnamed button, a
hidden button and a row with text inside; each name asked once for
each screen and its answer given to every visit; the placeholder
lists, with product names in camel case not taken as resource names;
the request's key, use, pictures and question; kept answers confirmed,
overturned, split, waiting for the model for complex questions, with
no model for complex questions, and with no answers; the level of each
agreed answer; and, in the analysis worker, the model for complex
questions asked to confirm agreed does-not-describe and not asked
where the first answers agree describes.

### Limits

- The model's answers were not measured on the recordings before
  building, so how often it is right, and what the check costs, are
  not known.
- Rule 2 depends on the tree's text and OCR: Flutter gives no text in
  the tree, and OCR misses short text such as a single digit, so a
  Flutter control that shows only a digit may be given 1.1.1 when 2.4.6
  applies. The finding still stands; only the criterion named may be
  wrong.
- Rule 3's lists matched nothing on these recordings, so they are
  tested only by unit tests.
- A name is judged once for each screen, from its first picture; where
  the same name means something else later on the same screen, the
  first answer is given.
- The impact, medium, is an assumption (see above).

## Step 29: links in an item's text

### The owner's decision (2026-11-19)

The companion app did not record where the links in an item's text are,
which the checks of links shown only by colour (1.4.1) and of link text
(2.4.4) need. The owner asked to fix that before recording the fixture
screen for links: "We have known that the recorder doesn't get link
placement for a while now. Maybe we need to fix that properly before we
do these fixture tests?" The owner then accepted this order: "I accept
the order, please start with step 1".

1. The companion app records the links in each item's text: where each
   is in the text, its kind, its address for a web link, and its boxes
   on the screen.
2. The recorder reads them into each item for the checks. A recording
   made before says that links were not recorded, not that there were
   none.
3. One phone recording, with the new companion app and the fixture
   app's "Recipes" screen, measured with no model: whether the boxes
   fall on the links' words in the screenshot. Then rules are proposed
   for the two colour checks.
4. The link text check (2.4.4) can use the same records, but is a
   separate step for the owner to choose.

### What Android gives (checked in cited documents)

- [AccessibilityNodeInfo.getText](https://developer.android.com/reference/kotlin/android/view/accessibility/AccessibilityNodeInfo)
  gives an item's text with its `ClickableSpan`s and `URLSpan`s
  replaced by ones an accessibility service can click. The companion
  app wrote the text with `toString()`, which drops them.
- The same page's `EXTRA_DATA_TEXT_CHARACTER_LOCATION_KEY`, asked for
  with `refreshWithExtraData`, gives a box for each of a run of
  characters. [Android's source for the class](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/core/java/android/view/accessibility/AccessibilityNodeInfo.java)
  says the box is null for a character that does not exist or is off
  the screen, that the boxes are changed by the display's
  magnification, and that one request asks for at most 20,000
  characters.
- [TextView's source](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/core/java/android/widget/TextView.java)
  offers that key and maps each character's box to screen pixels.
- Compose gives its links to accessibility services as such spans, as
  TalkBack reads them ([Google issue 253292081](https://issuetracker.google.com/issues/253292081));
  whether it gives their boxes is not known. A WebView's links are
  items of their own in the tree, measured on the fixture app's web
  page.

### As built (step 1)

- **The companion app, protocol 8.** Each item of an event or a tree
  has `links`, in the text's order, each with `start` and `end` in the
  text, in UTF-16 code units, `kind` (`url` for a `URLSpan`, otherwise
  `clickable`), `url` for a web link, `location`, `charactersLocated`
  and `boxes`. An item with no links has an empty list.
- **Where the links are.** For a tree's item with links, the companion
  app asks the app once for the characters from the first link's start
  to the last one's end, on a copy of the item, so the item being read
  is not changed. The boxes of each link's characters are joined into
  one box for each line, rounded out to whole pixels. `location` says
  what happened: `located`; `not-asked`, for an event's source item,
  read on the service's main thread, where a call to the app would hold
  up the service; `not-offered`, the item does not give its characters'
  places; `too-long`, more than 20,000 characters; `not-returned`,
  asked and nothing came back; or `text-changed`, the text changed
  before the places came back, so they are not given. A located link
  says how many of its characters had a box; one off the screen has
  none.
- **The recorder** accepts protocol 8 only, as for each protocol
  before, so the Android device panel offers "Install companion app".
  It keeps the links in each item (`AndroidTextLink`), and the
  validator checks that each link is within the item's text, in order,
  with boxes and a count only when located, and no more characters
  located than the link has. A recording made before has no `links`,
  read as not recorded.
- **The fixture extractor** lists each link's address among the text
  to read before committing.

### As built (step 2, 2026-11-19)

The owner asked: "Start step 2: have the recorder read the links into
each item for the checks".

- **Each item the checks read** (`PhoneCheckItem`) has `Links`: for each
  link, its place in the text and its words, its kind, a web link's
  address, its `location`, how many of its characters were located, and
  its boxes in the same pixels as the item's bounds (`PhoneTextLink`).
  The checks read a snapshot's window from its complete record, which
  holds the links, so nothing more is stored.
- **Not recorded is not none.** `LinksRecorded` is false, and `Links`
  null, for an item from a recording made before protocol 8; an item of
  a later recording with no links has an empty list.
- No check reads the links yet, and nothing on the screen changes: the
  rules for the colour checks come after step 3's measurement.

### What the standards say on colour alone (checked in cited documents)

- [WCAG 2.2's Understanding 1.4.1](https://www.w3.org/WAI/WCAG22/Understanding/use-of-color.html)
  says that colours differing "not only in their hue" but in lightness,
  at a contrast of "3:1 or greater", count as an additional visual
  distinction; and that a link "styled to appear no different than
  neighboring static text would not fail this success criterion".
- [Technique G183](https://www.w3.org/WAI/WCAG22/Techniques/general/G183)
  says that a link distinguished from the text around it only by colour
  needs a contrast of at least 3:1 against that text.
- [Failure F73](https://www.w3.org/WAI/WCAG22/Techniques/failures/F73)
  is a link identifiable by colour that is not "visually identifiable
  via some other means (e.g., underlined, bolded, italicized,
  sufficient difference in lightness, etc)", and a cue shown only on
  hover or focus "is still a failure".
- Neither page speaks of a selected state; the 3:1 note in
  Understanding 1.4.1 is about any content told apart by colour.
  [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) applies 1.4.1 to
  software.

### Measured on the phone (step 3, 2026-11-19)

A probe with no model read the recording of tests 60 to 62 (session
`20261119-164155-2f4f607d26a540d89710ce2bd9a7f7db`): the last snapshot
of the Recipes screen, taken from 14.98 seconds, and screenshot 6, taken
after it with the screen settled. The probe and its copies were deleted
afterwards. A pixel counted as ink at 30% or more of the way from white
to black, by its darkest channel; "blue" had blue over red by 30 or
more, "neutral" channels within 25 of each other.

**The link boxes fall on the links' words.** In both paragraphs, every
blue ink pixel was inside the link's box and every neutral one outside
it: 2,101 and 3,724 blue pixels inside, none outside; none of the
paragraphs' 3,677 and 2,592 neutral ink pixels inside. The blue ink
spanned 204 to 447 and 201 to 551 across, in boxes from 202 to 450 and
201 to 553, so each box held its link's ink with up to 3 pixels to
spare. Underlines and backgrounds:

| Paragraph | Link colour | Text colour | Link against text | Underline rows in the box | Stroke, link to text | Background in and out of the box |
| --- | --- | --- | --- | --- | --- | --- |
| `link_colour` | #1558B0 | #202124 | 2.34:1 | none | 0.95 | white, white |
| `link_underlined` | #1558B0 | #202124 | 2.34:1 | 1316 and 1317, in a box from 1271 to 1324 | 1.00 | white, white |

A row counted as an underline when its ink spanned 85% or more of the
ink's width. The stroke is the mean length of the runs of ink across
each row, at 50% ink or more, leaving out underline rows and runs of 40
pixels or more.

**The tabs.** The tree marks Salads and Lunch selected, and no other
tab. On the screenshot:

| Tab | Selected | Colour | Stroke | Underline rows |
| --- | --- | --- | --- | --- |
| Soups | no | #5F6368 | 4.12 | none |
| Salads | yes | #B0306A | 4.67 | none |
| Desserts | no | #5F6368 | 4.47 | none |
| Breakfast | no | #5F6368 | 4.36 | none |
| Lunch | yes | #B0306A | 6.32 | 1035 and 1036 |
| Dinner | no | #5F6368 | 3.74 | none |

The two colours are 1.00:1 against each other (both about 6:1 on
white), so only their hue differs. Against the mean of its row's
unselected tabs, Salads' stroke is 1.09 times, and the bold Lunch's
1.56 times. Every colour in the screenshot was exactly as the fixture
app sets it.

**Also seen.** The two paragraphs are recorded as focusable but not
clickable, where the fixture app's source comment expects the link
movement method to make them clickable.

### The owner's decision on the rules (2026-11-19)

Four questions were put, after the measurements above:

1. Should an underline, bold, 3:1 in lightness, a background 3:1 apart
   and, for tabs, a mark the others lack all count as a cue other than
   colour?
2. Bold at 1.3 times the stroke sits between the measured 1.09 (not
   bold) and 1.56 (bold), on one bold sample: accept it, or measure more
   first?
3. A warning or an error?
4. Italics cannot be detected: accept that limit?

The owner's answers, verbatim: "1: yes / 2: Accept for now / 3: If there
is no model involved, error / 4: yes". So every cue counts, bold is 1.3
times for now, each finding is an error, as no model is involved, and
italics are a known limit.

### As built (step 4)

Two checks, each finding one discrete issue, judged by rules with no
model, so each finding is an error (`PhoneColourOnlyRun`, measured by
`PhoneColourCues` in the contrast run's pass over each settled
snapshot):

- **`link-only-by-colour`, "Link told apart only by colour (WCAG
  1.4.1)".** For each link in an item's text from protocol 8, its ink
  in its boxes is compared with the item's text outside all its links'
  boxes. The ink is the commonest colour at least 10 Delta E from the
  commonest colour, its background, with at least 10 pixels of it. No
  difference of colour, in the ink or the background, is no finding, as
  Understanding 1.4.1 says. Otherwise the link passes on any of: 3:1 or
  more between the two inks, unrounded; an underline under every line
  of it, a row of ink across 85% or more of the ink's width in the lower
  half of its box; strokes 1.3 times as wide as the text's or more, the
  mean length of the runs of ink across each row, leaving out underline
  rows and runs as long as a line is high; or 3:1 or more between the
  backgrounds. A finding names its colours, its ratio, its stroke ratio
  and its background. Its identity is the item's with "link" and the
  link's place among the item's links, from 1.
- **`selected-only-by-colour`, "Selected state shown only by colour
  (WCAG 1.4.1)".** For each item the tree marks selected, with items
  beside it of the same class and parent not selected, its look is
  compared with theirs: their commonest ink, their commonest background
  and their mean stroke. No difference of colour is no finding. It
  passes on any of: 3:1 or more between the inks or between the
  backgrounds; strokes 1.3 times as wide as theirs or more; an
  underline none of them has; or a bar across 85% of its width, or down
  85% of its height, that none of them has. An item beside it with
  something drawn over it is left out; with none left, the item is not
  measured.
- **Not measured** (not run, saying why): a link whose place was not
  recorded, whose characters are not all placed, which is its item's
  whole text, or whose ink or text around it was not found; an item
  with the focus outline or a window above drawn over it, or not wholly
  on the picture; and anything shown only on a frame of the video,
  whose colours are approximate.
- **Not recorded:** a snapshot whose items' links were not recorded,
  from a companion app before protocol 8, is counted, and the Checks tab
  and the summary say the link check was not run for it.

Run on the owner's recording of tests 60 to 62 with no OCR and no model
(a probe, deleted afterwards): `link_colour` and `selected_colour` are
errors, and `link_underlined` and `selected_marked` have no finding
(measured). The plain link's strokes were 0.96 times the text's, and
the underlined link was found underlined. Salads' strokes were 0.99
times the others', and Lunch's 1.38 times, over the 1.3 for bold, and
Lunch was found underlined. These differ from step 3's 1.09 and 1.56,
as the built check takes as ink a pixel nearer the ink than the
background, and leaves out the underline's rows; Lunch's 1.38 is near
the line, so bold alone is a close pass there (measured).

The same probe found the non-text contrast check (step 5) giving an
error on both selected tabs, a state filled #FFFFFF against the
others' #FFFFFF. This was put to the owner as a separate issue.

#### The owner's decision on the selected tabs (2026-11-19)

The owner: "I don't see why this was tested at all. Non-text contrast
is for comparing different colours, not the same colour, identification
of the tab is separate as you said. Please fix".

As fixed: the non-text contrast check (WCAG 1.4.11) takes a control the
tree marks selected or checked to show its state by its fill only when
that fill differs from the commonest fill of the others in its row
(within the swatch's join limit, as for a state found from the picture).
A fill the same as theirs does not show the state, so there is no pair
of colours for 1.4.11 to compare; the control is judged as any control
is, by its visible text, icon or boundary. Whether the selected item is
told apart in some other way is for the use of colour check of selected
items (WCAG 1.4.1, above). This is in `PhoneNonTextContrastRun.States`
and tested in `tests/Recorder.Tests/PhoneNonTextContrastTests.cs`. The
evidence status: the probe measured both selected tabs filled #FFFFFF
like the others; that the fixed check gives them no state error is
inferred from the rule and the tests, and is to be seen on the phone
recording.

### Checked

In the Linux build environment: the solution builds with no warnings,
the tests pass, 2,778 of them, with the tests that need Windows failing
as before, and the companion app builds with its pinned build. The new tests read
a made-up tree with a web link not located, an item with no links and
two located links, one over two lines and one off the screen, an item
from before protocol 8, and each kind of bad link the validator
rejects. Run on the owner's phone in tests 60 to 62 (see the validation
plan): both of the Recipes screen's links were located, each as one
box inside its item's bounds.

For step 2, in the Linux build environment: the solution builds with no
warnings, the tests pass, 2,779 of them, with the tests that need
Windows failing as before, and the new tests read the made-up tree through the reader
the checks use, with each link's words, kind, address, place and boxes,
an item with no links, and every item of a tree from before protocol 8
as not recorded. A probe with no model, deleted afterwards, read the
owner's recording of the Recipes screen in the same way: "cooking
terms" and "kitchen safety notes", each located in full as one box,
and no links in the other 21 items of the window; and a window of the
committed fixture, from protocol 7, with its 16 items not recorded
(measured).

For step 4, in the Linux build environment: the solution builds with no
warnings, the tests pass, 2,799 of them, with the tests that need
Windows failing as before. The new tests draw made-up pictures in the
fixture app's colours: a plain link (an error), and its near misses
underlined, bold, 3:1 in lightness and on a background 3:1 apart (no
finding), a link just under bold (an error), a link in the text's
colour (no finding), and links not located, partly located, the whole
text, under the focus outline and on a video frame (not measured); a
selected tab told apart only by hue (an error), bold, underlined or
with a bar (no finding), drawn like the others (no finding), with an
item beside it covered (measured against the rest), itself covered or
on a video frame (not measured); and the run's errors, merging across
snapshots, not-run findings, and a recording from before protocol 8.
The owner's recording was read with no OCR and no model, as in "As
built (step 4)" above.
On the owner's PC, with the recorder installed from the pushed build
(2026-11-19), the owner saw the checks working in the Checks window on
the Recipes visit, with the chosen finding outlined on the "cooking
terms" link (seen by the owner). Whether the selected tabs are now free
of a non-text contrast state error was not reported, so is still
inferred from the rule and the tests.

### Limits

- Whether the boxes fall on the links' words was measured on one
  recording, of tests 60 to 62 (step 3).
- Italics are not detected (owner, 2026-11-19), so an italic link of
  another colour is an error a person has to check.
- Bold is 1.3 times the stroke, chosen "for now" on one bold sample.
- Marks other than an underline or a bar across or down the item, such
  as a dot or a tick, are not detected.
- An item under the focus outline or another window is not measured,
  nor a link or item shown only on a frame of the video.
- A selected state is checked only where the tree marks the item
  selected; an app that shows a state without marking it is not.
- An event's source item has its links but not where they are.
- Joining characters into lines assumes text read left to right; a
  right-to-left link may be given a box for each character or a few
  (inferred, not measured).
- The boxes are changed by the display's magnification, as Android
  says; the window-coordinate key that is not is newer and not used.
- A web link's address is recorded, as the text is; it may hold
  something personal, as text may.

## Step 30: link purpose (2.4.4 and 2.4.9)

### The owner's requests (2026-11-19)

The owner: "Start on the link text check (WCAG 2.4.4) using the same
link records, measured with no model first". Then: "Remember to include
the check for 2.4.9 as well when checks are set for AAA. When AA, please
give a warning if 2.4.9 would fail and refer to 5.2.4 (accessibility
supported)".

### What the standards say (checked in cited documents)

- [Understanding 2.4.4](https://www.w3.org/WAI/WCAG22/Understanding/link-purpose-in-context.html)
  (level A): the purpose of each link "can be determined from the link
  text alone or from the link text together with its programmatically
  determined link context, except where the purpose of the link would be
  ambiguous to users in general". The context can be "the same
  sentence, paragraph, list item, or table cell as the link"; failure
  F63 is link context "only in content that is not related to the
  link".
- [Understanding 2.4.9](https://www.w3.org/WAI/WCAG22/Understanding/link-purpose-link-only.html)
  (level AAA): "A mechanism is available to allow the purpose of each
  link to be identified from link text alone", with the same exception.
  It is for links met "out of context, such as when the user agent
  provides a list of all the links on a page". Failure F84 is "a
  non-specific link such as "click here" or "more" without a
  mechanism to change the link text to specific text".
- [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) applies 2.4.4 to
  software as written, and adds that in software "a "link" is any user
  interface control that behaves like a hypertext link".
- [WCAG 2.2's conformance requirement 5.2.4](https://www.w3.org/TR/WCAG22/#cc4):
  "Only accessibility-supported ways of using technologies are relied
  upon to satisfy the success criteria".
- TalkBack has a Links reading control that moves "from one link to
  the next", and a Links item in the TalkBack menu
  ([Navigate your device with TalkBack](http://support.google.com/accessibility/android/answer/6006598),
  [Use the TalkBack menu and reading controls](http://support.google.com/accessibility/android/answer/6007066)).
  Whether TalkBack speaks the link's sentence when it reaches a link
  this way is not said there and has not been recorded (not measured).

### Measured on the phone (2026-11-19)

A probe with no model and no OCR, deleted afterwards with its copy of
the recording, read every snapshot of the owner's recording of the
Recipes screen (tests 60 to 62) through `PhoneCheckWindow`, 14
snapshots holding their windows (measured):

- Two links, "cooking terms" and "kitchen safety notes", each of kind
  "clickable" (a ClickableSpan), so with no address recorded, each
  inside a sentence that is the whole text of its own TextView. Each
  TextView is focusable and not clickable; its parent is a layout with
  no text of its own.
- No item of another app's window had a link.
- The six course tabs are clickable TextViews with no link in their
  text. They choose what the screen shows rather than go somewhere, so
  they are taken not to behave like links (inferred).

Both links' words say what they lead to, so the recording holds near
misses only. None of the rules proposed below would make a finding on
it (inferred, by applying them to the measured records).

### The rules proposed (for the owner to choose)

1. **Which links.** The links recorded in an item's text (step 29),
   whether located on the screen or not, as the check reads the words,
   not the picture. A control on its own that behaves like a link is
   not judged, as the tree does not tell it apart from a button.
2. **A link's context for 2.4.4.** The rest of its own item's text: the
   same sentence or paragraph, as in G53 and H78. Text in other items,
   such as a heading above, is not taken as context (F63).
3. **No model.** Each finding comes from the words alone:
   - A link with no letter or digit in its words: an error under 2.4.4
     (and 2.4.9 at AAA).
   - A link whose words are a non-specific phrase from a fixed list,
     such as "click here", "here", "more", "read more", "learn more",
     "details" or "link", ignoring case and end punctuation:
     - with no other words in its item: an error under 2.4.4, its
       context being only in other items (F63);
     - with other words in its item: no 2.4.4 finding, as the sentence
       gives the context.
   - Two links in one window with the same words and different recorded
     addresses: a 2.4.9 finding; under 2.4.4 too only when their items'
     other words are the same as well.
   The exception, a purpose "ambiguous to users in general", needs
   judgement, so each finding says a person can set it aside on that
   ground.
4. **2.4.9 by audit level** (owner, 2026-11-19). At AAA, a
   non-specific link, or the same words for different addresses, is an
   error under 2.4.9 (F84), even when its sentence passes 2.4.4. At AA,
   the same is a warning that 2.4.9 would fail, referring to 5.2.4: the
   pass under 2.4.4 relies on TalkBack giving the sentence with the
   link, which is not known for links reached through TalkBack's Links
   reading control or menu.
5. **Language.** The list of non-specific phrases is English. The app's
   language is not recorded, so a link in another language gets only
   the rules on no words and on the same words for different addresses,
   and the finding says the phrase list is English.
6. **The fixture.** The Recipes screen has the near misses. A fault
   needs the fixture app changed and a new phone recording: a "Read
   more" link that is its item's whole text, after a paragraph (2.4.4
   error); "To see the full menu, tap here." (2.4.4 passes; 2.4.9 warns
   at AA and is an error at AAA); and two "Details" links to different
   addresses.

### The owner's decision on the rules (2026-11-19)

The owner: "The rules are ok, but specifically duplicate link text
needs to called out because of screen-readers. So, separate error to
just being a fail for lack of context at 2.4.9".

So the same words for different addresses is a check of its own,
apart from the non-specific link, as a screen reader's list of links
gives the same words for both. It is read here as keeping rule 4's
levels: an error at AAA under 2.4.9, and a warning at AA referring to
5.2.4 (inferred from "The rules are ok"); when the items' other words
are the same as well, it is an error under 2.4.4 at both levels. Making
it an error at AA too is a one-line change.

### As built

`PhoneLinkPurposeChecks` in `Recorder.Session`, run on each window's
items with the other per-window checks, with no model and no OCR. Only
items shown to the user (visible, with bounds) are looked at, and every
link recorded in their text. The words are compared after lower-casing,
joining white space and trimming punctuation and symbols from the ends.

| Check | Finding | Level |
| --- | --- | --- |
| `link-no-text`, "Link with no words (WCAG 2.4.4)" | A link with no letter or digit in its words | Error; at AAA it says 2.4.9 fails as well |
| `link-no-context`, "Non-specific link with no context (WCAG 2.4.4)" | A non-specific phrase that is all of its item's text (F63) | Error |
| `link-not-specific`, "Link text not specific on its own (WCAG 2.4.9, AAA)" | A non-specific phrase with other words in its item (F84) | Warning at AA, referring to 5.2.4; error at AAA |
| `link-same-text-and-context`, "Same link text and context for different addresses (WCAG 2.4.4)" | The same words and the same other words in the item as a link to a different address | Error |
| `link-same-text`, "Same link text for different addresses (WCAG 2.4.9, AAA)" | The same words as a link to a different address, its other words differing | Warning at AA, referring to 5.2.4; error at AAA |

The non-specific phrases, in English: click, click here, tap, tap here,
here, this, this link, link, go, continue, more, read more, learn more,
find out more, see more, more info, more information, details, more
details, info.

Each finding says a person can set it aside if the link's purpose would
be ambiguous to users in general. One item's findings for one check are
merged into one result. The run counts the links read and the items
with text whose links were not recorded, and the Summary has a section
"Link purpose (WCAG 2.4.4, and 2.4.9 at AAA), five checks". The
guidance file has an entry for each check, citing 2.4.4, 2.4.9, G91,
G53, F63, F84, 5.2.4 and TalkBack's Links reading control.

The fixture app has a new screen, "Kitchen help" (`link-purpose`), with
a fault and a near miss for the non-specific and duplicate checks
(android-fixtures.md, "The link purpose screen"). It waits for its
first recording.

### Checked

- Rule tests, `PhoneLinkPurposeChecksTests`, on made-up windows: each
  check's fault and near miss, the AA and AAA levels, the same words to
  the same address making no finding, links with no address not
  compared, and items not shown not looked at (measured, the tests
  pass).
- On the Recipes recording's measured records the rules make no
  finding (inferred, by applying them; the checks have not yet been run
  on a recording).
- On the owner's PC, with the recorder installed from the pushed build
  (2026-11-19), the owner recorded the tour of the Kitchen help screen
  and saw the checks working in the Checks window at AA. Seen in the
  owner's screenshot: "Non-specific link with no context (WCAG 2.4.4): 1
  error" on "Read more", with that link outlined on the phone's picture,
  and "Link text not specific on its own (WCAG 2.4.9, AAA): 3 warnings",
  the number expected for "here" and the two "Details" links (seen by
  the owner). The same link text findings, the near misses and the AAA
  level were below or outside the screenshot, so are not yet seen.

### Limits

- The phrase list is English; another language gets only the checks on
  no words and on the same words.
- Links with no recorded address, such as ClickableSpans, are not
  compared for sameness, as two of them cannot be told to go to
  different places.
- Links are compared within one window, not across a screen's windows
  or visits.
- A content description over an item's text is not considered, so a
  link whose item is described differently is judged on its text.
- A control on its own that behaves like a link is not judged.
- Whether TalkBack speaks a link's sentence when it is reached through
  the Links reading control is not recorded (not measured).

## Decisions on the open questions (2026-11-11)

8. **Screens titled by their content are separate for now.** A
   conversation titled with a person's name, or an inbox titled
   "Inbox (3)", is a new screen for each title (owner). This is to be
   looked at again with the client's app and with matching across
   recordings.
9. **Every app seen is checked, grouped by app** (owner), so the
   client's app and anything it opens (a browser, a share sheet, a
   permission dialog) are each shown as what they are.

[atf-touch]: https://github.com/google/Accessibility-Test-Framework-for-Android/blob/c65cab02b2a845c29c3da100d6adefd345a144e3/src/main/java/com/google/android/apps/common/testing/accessibility/framework/checks/TouchTargetSizeCheck.java
[atf-speakable]: https://github.com/google/Accessibility-Test-Framework-for-Android/blob/c65cab02b2a845c29c3da100d6adefd345a144e3/src/main/java/com/google/android/apps/common/testing/accessibility/framework/checks/SpeakableTextPresentCheck.java
[atf-dup-text]: https://github.com/google/Accessibility-Test-Framework-for-Android/blob/c65cab02b2a845c29c3da100d6adefd345a144e3/src/main/java/com/google/android/apps/common/testing/accessibility/framework/checks/DuplicateSpeakableTextCheck.java
[atf-dup-bounds]: https://github.com/google/Accessibility-Test-Framework-for-Android/blob/c65cab02b2a845c29c3da100d6adefd345a144e3/src/main/java/com/google/android/apps/common/testing/accessibility/framework/checks/DuplicateClickableBoundsCheck.java
[atf-editable]: https://github.com/google/Accessibility-Test-Framework-for-Android/blob/c65cab02b2a845c29c3da100d6adefd345a144e3/src/main/java/com/google/android/apps/common/testing/accessibility/framework/checks/EditableContentDescCheck.java
[atf-redundant]: https://github.com/google/Accessibility-Test-Framework-for-Android/blob/c65cab02b2a845c29c3da100d6adefd345a144e3/src/main/java/com/google/android/apps/common/testing/accessibility/framework/checks/RedundantDescriptionCheck.java
[atf-class]: https://github.com/google/Accessibility-Test-Framework-for-Android/blob/c65cab02b2a845c29c3da100d6adefd345a144e3/src/main/java/com/google/android/apps/common/testing/accessibility/framework/checks/ClassNameCheck.java
[atf-traversal]: https://github.com/google/Accessibility-Test-Framework-for-Android/blob/c65cab02b2a845c29c3da100d6adefd345a144e3/src/main/java/com/google/android/apps/common/testing/accessibility/framework/checks/TraversalOrderCheck.java
[atf-text-contrast]: https://github.com/google/Accessibility-Test-Framework-for-Android/blob/c65cab02b2a845c29c3da100d6adefd345a144e3/src/main/java/com/google/android/apps/common/testing/accessibility/framework/checks/TextContrastCheck.java
[atf-image-contrast]: https://github.com/google/Accessibility-Test-Framework-for-Android/blob/c65cab02b2a845c29c3da100d6adefd345a144e3/src/main/java/com/google/android/apps/common/testing/accessibility/framework/checks/ImageContrastCheck.java
[atf-clickable-span]: https://github.com/google/Accessibility-Test-Framework-for-Android/blob/c65cab02b2a845c29c3da100d6adefd345a144e3/src/main/java/com/google/android/apps/common/testing/accessibility/framework/checks/ClickableSpanCheck.java
