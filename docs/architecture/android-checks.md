# Android Checks: Apps, Screens and Findings

## Status

Proposed 2026-11-11. Step 1 of "Build order", screens and visits, is
built (see "As built: step 1"), with rules added on 2026-11-12 for
screens drawn in one window, measured in the client's app. Step 2, the
display density and each app's version, is built (see "As built: step
2") and not yet checked on the phone. No check is built yet. The owner's
decisions of 2026-11-11 are in "Decisions (2026-11-11)" and "Decisions
on the open questions (2026-11-11)". Before any check is assigned a
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
   drawn, changes nothing.

Scrolling, focus moving, text typed, items changing, and content changes
that keep the window, its class name, its title and its first fixed text
do not start a visit.

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

A **finding** is one check's result for one item on one screen, at one
level. It lists every settled snapshot in which it was seen, with its
time, so the player can move to the first and step through the rest.
The same item failing the same check on two visits to the same screen
is one finding with two visits.

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
"warning" and "not run"; the recorder keeps those words.

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
by app, then screen, then check. Each row says the check, the level,
the item, the measured value and the state, such as "Touch target under
48dp: 'Navigate up' button, 40 by 40 dp, Safety and emergency
(Settings)". Choosing a row moves the playhead to the finding's first
snapshot, draws its bounds on the phone's picture, and selects the item
in the "Accessibility tree" tab; "Next time" and "Previous time" step
through the snapshots it was seen in. Each screen's row says how its
title was found. Text wraps in the tab; there is no horizontal
scrollbar.

The checks run when the recording is opened, from its stored snapshots
and frames, and their results are kept beside the recording so they are
not run again until the recorder's check version changes (inference:
the time they take is to be measured; a recording of a few minutes has
some hundreds of snapshots).

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
  visit at the snapshot that shows it. A visit whose window, app,
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
  a screen or an app. The visit at the playhead is marked; "Show current
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
  would start a new visit to the same screen.

## As built: step 2

Built 2026-11-12 in `src/Recorder.Collectors.Android/AndroidDeviceCollector.cs`
(records in `src/Recorder.Contracts/AndroidDeviceRecords.cs`, channel
`device.android`) and `src/Recorder.Session/PhoneDeviceTimeline.cs`, with
tests in `tests/Recorder.Tests/AndroidDeviceTests.cs`. Not yet checked on
the phone: what `wm density` and `dumpsys package` print below is
checked in AOSP's source (main branch, read 2026-11-12), not measured
on the Pixel 10 Pro XL, so the formats are inference until the test in
the validation plan.

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

Measured before step 2, in the client's recording (session
`20261112-005243-5528d67531dd4441a50b2c2985350c59`): "Display size" was
not set (`display_density_forced` null) and the display mode was 1080 by
2404 pixels. The physical density was not recorded then.

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
