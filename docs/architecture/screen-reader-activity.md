# Screen Reader Activity

## Status

Proposed 2026-10-07, agreed 2026-10-07, not built. Listed in
[outstanding work](analysis-outstanding-work.md). The owner accepted a
low-level keyboard hook for recording on 2026-10-07. This design follows
[assistive technology detection](assistive-technology-detection.md), which
records which screen reader is running, and the
[screen reader keys validation](../validation/screen-reader-keys-2026-10-07.md),
which measured what reaches the recorder while NVDA runs.

## The problem

A screen reader user moves through a page in several ways: Tab and the
arrow keys, single-letter commands for headings, landmarks, links, and
other elements, and lists of elements pulled from the page to choose
from. An auditor viewing a recording needs to see what the participant
did and what the screen reader did in response.

The validation established, with NVDA 2026.2 on the target machine:

- The keys NVDA keeps as commands (quick navigation, the browse mode
  arrows, NVDA+key) are missing from raw input, the recorder's and an
  independent capture alike. A low-level keyboard hook installed after
  NVDA's saw every one of them.
- NVDA kept each physical Tab and injected a Tab in its place. The
  injected key is in raw input with no device handle, and the page
  received it.
- Keys in NVDA's own windows, such as the elements list, reach raw input,
  and UI Automation records those windows.
- Browse mode navigation left no change of focus or selection in the page.

## What is recorded and what is inferred

Recorded at runtime, each with its basis:

1. Every key event, from a low-level keyboard hook (new).
2. Every key event that reached the input system, with its device, from
   raw input (built).
3. Every key event the page received, from the instrumented Chromium's
   dispatch records (built).
4. The screen reader's own windows, from UI Automation (built).
5. Which screen reader runs, its version, and its readable settings, from
   [assistive technology detection](assistive-technology-detection.md)
   (designed).
6. The system's sound, which includes the screen reader's speech (built).

Derived from those records, by a stated rule, at playback:

- Each key's disposition: kept by the screen reader, passed on, or
  injected by software ("Key disposition").

Inferred, each shown as an inference with its basis:

- Which command a kept key was, from the screen reader's product, version,
  keyboard layout, and key commands.
- Whether the screen reader was in browse or focus mode, from the keys it
  kept and passed.
- What the screen reader said, by local speech recognition of the audio
  (analysis work, not designed here).
- Where the screen reader's reading position was (not designed here; see
  "Not in this design").

## The keyboard hook

A collector, `windows.keyboard-hook`, installs a `WH_KEYBOARD_LL` hook on
its own thread and records every call.

- It observes only: every call passes the key to the next hook unchanged,
  and the collector never blocks, delays, or alters a key.
- The hook "is called in the context of the thread that installed it",
  which "must have a message loop"
  ([Microsoft, LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)),
  so it runs in the recorder's own process and nothing is loaded into the
  screen reader.
- The callback copies the event into a bounded queue and returns; the
  record is written from another thread. A hook that exceeds the
  `LowLevelHooksTimeout` "is silently removed" on Windows 7 and later, with
  "no way for the application to know", and the timeout is at most 1000 ms
  on Windows 10 version 1709 and later (same source).
- Record `hook-keyboard` on channel `input.keyboard-hook`: the virtual
  key, scan code, the flags (up, extended, injected, injected at lower
  integrity), the extra information, the event's own time, and the receipt
  time.
- Order: `SetWindowsHookEx` "always installs a hook procedure at the
  beginning of a hook chain"
  ([Microsoft, about hooks](https://learn.microsoft.com/en-us/windows/win32/winmsg/about-hooks)),
  and a screen reader's hook that keeps a key does not pass it on. The
  collector therefore reinstalls its hook when assistive technology
  detection sees a screen reader start, so that its hook is first again,
  and records `hook-installed` with the reason (start of recording, screen
  reader started, hook lost).
- Loss: the collector compares its records with raw input as it runs. A
  raw input key from a device with no hook record of the same key within
  the join window means the hook was removed; the collector reinstalls it
  and records `hook-installed` with reason "hook lost" and the time of the
  last key it saw. The gap is shown in the player.
- Keys are recorded verbatim, as raw input already records them,
  including typed passwords; the hook adds the keys the screen reader
  keeps. It changes the volume of keyboard evidence, not its kind, under
  the existing privacy and data-handling policy.
- Raw input stays as built: it is the only record of the device a key came
  from.

## Key disposition

Derived at playback from the records, with the rule versioned so that a
recording can be read again under a later rule.

- Join: a hook record joins a raw input record of the same key and
  direction within 300 ms, where an injected hook record joins only a raw
  input record with no device handle, and a physical one only a record
  with a device handle. A page `keydown` joins the raw input record it
  follows within the window used by the validation scripts.
- Kept: a physical key with a hook record and no raw input record. When a
  screen reader is running, this is attributed to it as an inference: the
  records do not name the process that kept the key.
- Passed: a key with a raw input record from a device; "received by the
  page" when a page `keydown` joins it.
- Injected: a hook record with the injected flag, or a raw input record
  with no device handle. Joined to the screen reader when it follows a
  kept key of the same virtual key within the window, as NVDA's Tab did;
  otherwise the injecting process is unknown.
- Unjoined records are shown as they are, with the reason.

## The screen reader's windows

UI Automation already records them. At playback, the records whose
process is a detected screen reader are grouped into episodes, such as
the elements list: the window opening and closing, the items focused and
their names, the text typed into its filter, and the button pressed. The
keys typed into such a window are passed keys, shown with the episode.

## Settings and key commands

Assistive technology detection reads the screen reader's readable
configuration at the start of a recording, without changing it. For NVDA,
a portable copy keeps "all settings and add-ons in a directory called
userConfig, found in the NVDA directory", and an installed copy in the
user's profile
([NVDA user guide](https://download.nvaccess.org/releases/2026.2/documentation/userGuide.html)).
The values used here are the keyboard layout, the NVDA modifier keys,
the custom key commands, and the browse mode options.

The commands of each product and version are kept as data in the player,
with their source, starting with NVDA's documented browse mode commands
and the elements list. A kept key is labelled with its command, for
example "H: next heading", as an inference from the product, version,
layout, and custom commands recorded; with no match it is shown as a kept
key.

## Speech audio

The system sound already carries the screen reader's speech, mixed with
the page's sound. Capturing one process's sound needs
`AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`, whose minimum supported
client is Windows 10 build 20348
([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-audioclient_activation_type));
the target machine, build 19045, cannot. On such builds speech stays in
the system sound; where the build allows, the screen reader's sound is
recorded as its own channel. Speech recognition is analysis work, run
locally, and is not designed here.

## The player

A "Screen reader" lane on the timeline shows:

- each key, with its disposition, and for a kept key its inferred
  command and the basis;
- the screen reader's window episodes;
- an inferred mode band: where the screen reader keeps keys that would
  otherwise reach the page, browse mode; where they pass, focus mode; with
  the basis;
- gaps where the hook was not recording, and where it was not first in
  the chain (a screen reader started before the hook was reinstalled).

## Not in this design

- The screen reader's reading position. Browse mode navigation left no
  trace in the page's focus or selection. Requests the screen reader
  makes of Chromium, such as scrolling an element into view, all pass
  through `RenderAccessibilityImpl::PerformAction`
  (`content/renderer/accessibility/render_accessibility_impl.cc`, line 328
  in the Windows checkout); whether NVDA makes any during browse mode
  navigation is untested. With speech recognition matched to the
  recorded accessibility tree, they are the basis for a later design.
- Braille output.
- Screen readers other than NVDA, until each is tested.

## Questions to settle

- Whether a low-level mouse hook is added on the same terms, for injected
  mouse input and the screen reader's mouse commands.
- Whether to record `PerformAction` requests in the instrumented Chromium,
  after a test of what NVDA requests during browse mode navigation.
- The format and home of the command data, and how a version without its
  own data is handled.
- The join window, if the target machine shows longer delays than the
  validation.

## Required tests

- Unit tests of the `hook-keyboard` and `hook-installed` contracts and
  validator; of the join and the dispositions from generated sequences,
  including a kept physical Tab replaced by an injected one, keys in a
  screen reader window, and unjoined records; of hook loss detection; and
  of command labelling against the command data.
- Integration tests on Windows: the hook installs without administrator
  privileges, never blocks a key, and records the injected flags; the
  callback's duration stays far below the timeout under load.
- A system test on the target machine, extending
  `scripts/Test-ScreenReaderKeys.ps1`: every key of the script's hook is in
  the recorder's hook with the same disposition; NVDA started after the
  recording begins, and restarted during it, is followed by a reinstalled
  hook with no keys lost after it; keys to an elevated window are
  checked and the result recorded.
