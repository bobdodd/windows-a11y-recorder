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
checked on the development PC on 2026-11-12 (tests 36 and 37 passed). The owner's
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
   model is asked.
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
   test data model.
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
   the PC.
9. **Label in Name** (WCAG 2.5.3), a control's content description
   against the text inside it (see "Step 9: Label in Name"). Built, and
   checked on the PC.

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
  title change (inference: the app is drawn with Jetpack Compose);
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
- **The 24dp level** counts a target's own ancestors and descendants as
  the same place to touch, not as other targets (inference: WCAG 2.5.8
  speaks of separate targets).
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
  are in content descriptions (Compose). No item is checkable, and none
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
  essential presentation) are not judged."
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
- **Limits.** 2.5.5's exceptions are not judged: a target with an
  equivalent of 44 dp, one in text, one sized by Android and one whose
  size is essential are all reported, and need a person to set aside.
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
  (a custom view, or a Compose image left out of the tree) is not
  looked at. Text found by OCR in no item is a separate candidate, not
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
