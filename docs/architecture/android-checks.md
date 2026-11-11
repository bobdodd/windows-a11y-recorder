# Android Checks: Apps, Screens and Findings

## Status

Proposed 2026-11-11. Nothing is built. The owner's decisions of
2026-11-11 are in "Decisions (2026-11-11)"; the questions still open are
in "Open questions". Before any check is assigned a result, the
recording has to say which app and which screen each moment belongs to,
so that comes first in "Build order".

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

Not recorded, and needed:

- **Density.** Bounds are in pixels; dp needs the display density. Only
  a forced "Display size" setting is recorded (checked in source:
  `AndroidSettingsRecords`, `display_density_forced`), not the density
  in use.
- **The app's version.** Only the companion app's version is recorded
  (checked in source: `RecorderLink`). A client report needs the
  version of the app under test.
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
package seen, with `adb shell dumpsys package <package>`, which prints
`versionName` and `versionCode` (inference: the command's output is not
documented as an interface, so the recorder keeps the lines it read).

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
   from the app whose class name or text differs from the visit's,
   including a pane appearing or its title changing. This covers apps
   that change screens inside one window.
3. **Another app comes to the front,** as above.

Scrolling, focus moving, text typed, items changing, and content changes
that keep the window, its class name and its title do not start a
visit.

### What names a screen

Within a recording, two visits are to the same screen when they share a
**screen key**:

- the app's package,
- the class name from the window-state event that started the visit
  (the activity, for an activity window),
- the **screen title**: the window title when the app's window has one;
  otherwise the latest pane title; otherwise the text of the first
  heading, in the tree's order, in the app's window.

The player names each screen by its title and app, such as "Safety and
emergency (Settings)", and shows which of the three gave the title.

When none of the three gives a title, the screen is named by its
activity and a number in order of first visit, such as "SubSettings,
untitled screen 2", and the key adds the sorted set of view IDs in the
app's window, so different untitled screens are kept apart. Where there
are no view IDs either, visits with the same activity and no title are
treated as one screen; the player says so. This is a known limit for
apps that expose neither titles nor IDs (see "Open questions", 1).

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
window has the same bounds and transition time as in the next snapshot
of that window, or as at the end of the visit. A snapshot taken while a
window slides in, as at 16.57 s above, is shown in the tree view as
now, but no check uses it.

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

## Open questions

1. **Screens whose title changes with their content.** A conversation
   titled with a person's name, or an inbox titled "Inbox (3)", gives a
   new title, and so a new screen, for each name or count. Proposed:
   keep that for the first version and look at what the client's app
   does; matching such screens by structure belongs with matching
   across recordings.
2. **Which apps are checked.** Proposed: every app seen, grouped by
   app, so the client's app and anything it opens (a browser, a share
   sheet, a permission dialog) are each visible as what they are.

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
