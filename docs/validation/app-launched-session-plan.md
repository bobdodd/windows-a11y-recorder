# Application-Launched Session Validation

## Purpose

Every earlier browser validation ran through the validation capture host,
which starts instrumented Chromium with a DevTools debugging port and drives
the fixture pages through that port. The recorder application never adds a
debugging port, and a person using it produces input through Windows rather
than through DevTools. This validation records a session started from the
recorder application itself and checks that the browser, input, UI
Automation, foreground-window, and desktop-frame channels record the same
interaction consistently.

## What the run does

`scripts\Run-AppSessionValidation.ps1`:

1. Runs the managed tests and builds `Recorder.App`.
2. Serves a fixture page from a loopback port. The page writes a cookie from
   script, fetches a data response, hosts an open shadow root and a `::before`
   pseudo-element, and holds a button, a labelled text field, and a second
   button. Its title reports readiness after the fetch completes.
3. Starts the recorder application with `DOTNET_ROOT` set to the folder of
   the .NET SDK it was built with, since the application is
   framework-dependent and a per-user .NET installation is not found
   otherwise. It then sets the application through UI Automation: the
   output folder, the Chromium executable, the fixture as starting website,
   keyboard and mouse, UI Automation, foreground window, and desktop frame
   capture on, microphone and system audio off, and browser evidence on.
4. Starts the recording with the application's Start button, minimizes the
   recorder window, and waits for the fixture page to report readiness.
5. Confirms from the process command lines that the application launched
   Chromium with the recorder bootstrap switch and without a debugging port.
6. Brings Chromium to the foreground, reads the screen rectangles of the three
   controls from UI Automation, and confirms that no other window covers
   them. The foreground change uses Win32 calls, attaching to the input
   queue of the current foreground window for the call, because the
   Chromium top-level window is not keyboard focusable through UI Automation
   and `SetFocus` on it fails. No input is sent to make the change.
7. Injects Windows input with `SendInput`: a click on the button, a click on
   the text field, the keys `a`, `1`, `1`, `y`, and Tab. Every injected input
   carries the extra-information value `0x41313159`, which the raw input
   collector records, so injected input can be told apart from any other
   input during the run.
8. Stops the recording with the Stop button and waits for the application to
   report that the session files were verified and to load the session into
   playback.
9. Closes the application through its window, which runs its normal shutdown.
10. Checks the archive validation report and applies the same evidence-loss
    rule as the Blink validation: browser records reported as lost, or events
    refused by the event sink, fail the run.
11. Runs `scripts\Verify-AppSessionEvidence.ps1` on the session.

## What the verifier requires

- One start and one stop for each Windows collector.
- One committed primary main-frame navigation to the fixture, rendered by a
  process started from the instrumented Chromium executable.
- From that renderer: the script cookie write, the data request and its
  completion, an open shadow root, a layout checkpoint that contains a shadow
  root and a pseudo-element, a completed accessibility checkpoint, and the
  listener registration on the button.
- Two marked left-button presses and releases, and ten marked key records.
  Each press is inside the rectangle UI Automation reported for its control,
  with an instrumented Chromium process in the foreground.
- For each injected input, a trusted browser dispatch of the matching event
  reaching the matching element, recorded from 250 ms before to 2 s after the
  raw input record: `mousedown` and `click` on the button, `mousedown` and
  five `keydown` dispatches on the text field, and the Tab `keyup` on the
  second button.
- Focus changes caused by the input, with no script location, script world,
  or execution world: by mouse to the button and the text field, and forward
  to the second button. The recorded `focusTrigger` is Blink's own value. It
  is `user-gesture` for the Tab move but `script` for the two mouse moves,
  because Blink's ordinary mouse focus path in `MouseEventManager` leaves the
  trigger at its default of `script`. The first app-launched run, on
  2026-09-25, failed on this expectation; the recorder had logged Blink's
  value correctly.
- User edits of the text field whose last recorded value is `a11y`.
- UI Automation focus changes to each of the three controls, by name, in an
  instrumented Chromium process, after the input that moved focus.
- A foreground-window record showing Chromium in front before the first
  injected input, and at least one desktop frame recorded while the input
  ran.

The verifier reports the measured time from each raw input record to its
dispatch and to its UI Automation focus change. These are measurements from
one run on one machine. They are not a latency contract.

## Limits

- Injected input enters the Windows input stream after the device driver.
  The run shows that such input is recorded by every channel; it does not
  exercise a physical keyboard or mouse, and the raw input device handle of
  injected input is zero.
- The capture host declares per-monitor DPI awareness. The recorder
  application uses the WPF default, system DPI awareness. On a scaled or
  mixed-DPI display, the cursor positions the raw input collector samples and
  the rectangles UI Automation reports to the application may therefore be in
  different coordinate spaces from those of a capture-host session. The run
  makes its own coordinates physical pixels and reports the displays and
  system DPI it ran with; results from a single display at 100 percent scale do not establish
  behavior on scaled displays.
- The run does not rebuild Chromium. It uses the build that
  `scripts\Run-BlinkValidation.ps1` produced from the same revision of the
  `chromium` directory. The recorder refuses a build with a different
  protocol version.
- UI Automation events can be dropped when the collector's observation queue
  is full. The run reports every omission the session recorded; only lost
  browser evidence fails the run.
- The run uses no screen reader. Screen-reader sessions remain a separate
  roadmap item.
- Browser dispatch and raw input are timestamped by different threads. The
  accepted window allows dispatch records to precede their raw input record
  by up to 250 ms.

## Result

The application-launched session completed the reference Windows procedure on
September 25, 2026, using repository commit `afaf0c9`. The run script exited
with code 0 after:

- Passing the managed recorder test suite and building the recorder
  application.
- Starting the application, which launched instrumented Chromium with the
  recorder bootstrap switch and without a debugging port.
- Recording the fixture page with keyboard and mouse, UI Automation,
  foreground window, desktop frame, and browser evidence.
- Injecting the marked clicks, typing, and Tab, and stopping the recording
  through the application.
- Loading the finished session into the application's playback.
- Passing archive validation, the browser evidence-loss rule, and every
  requirement of `Verify-AppSessionEvidence.ps1`.

The validated session is:

`C:\Users\Public\Downloads\A11yRecorderAppSession\sessions\20260925-123720-1874f18beabe4890a2e3e6108cab7056`

The run reported:

- `APP_STOP_STATUS=Recording completed and session files verified.`
- `APP_PLAYBACK_STATUS=20260925-123720-1874f18beabe4890a2e3e6108cab7056 | 44 frames | 31,010 events | 0 audio tracks`
- `APP_PLAYBACK_NAVIGATIONS=5`
- `DISPLAYS=\\.\DISPLAY1 1920x1080 at (0, 0)`
- `SYSTEM_DPI=96`
- `ARCHIVE_VALID=True`
- `EVENTS_VALIDATED=31010`
- `ARTIFACTS_VALIDATED=46`
- `SINK_REFUSED_EVENTS=0`
- `BROWSER_OMITTED_RECORDS=0`
- `OTHER_OMISSIONS=` (none)

The verifier found 12 instrumented Chromium processes, the fixture renderer
as process 31324, 6 marked mouse records, and 10 marked key records. It
reported 7 shadow roots, 10 layout checkpoints, 10 accessibility checkpoints,
15 desktop frames during the input, and no omission kinds. Focus reached
nodes 5, 7, and 18, and the text field's last user-edit value was `a11y`
after 4 user edits.

Measured delays from the raw input record:

| Measurement | Delay (ms) |
| --- | --- |
| Button press to trusted `mousedown` | 2.16 |
| Button release to trusted `click` | 2.36 |
| Text field press to trusted `mousedown` | 2.73 |
| First key to trusted `keydown` | 1.29 |
| Button press to UI Automation focus | 24.88 |
| Text field press to UI Automation focus | 30.56 |
| Tab press to UI Automation focus | 34.44 |

These delays are single measurements from one run on one machine, a single
1920x1080 display at 96 DPI, with injected rather than physical input. They
show that the channels agree on this machine; they are not a latency
characterisation.

Three earlier attempts on the same day failed before this result, none of
them because of a recorder defect:

1. The application did not start, because the per-user .NET installation was
   not found without `DOTNET_ROOT`. The runner now sets it for the process it
   launches.
2. UI Automation `SetFocus` on the Chromium top-level window threw, since
   that window is not keyboard focusable. The runner now moves the
   foreground with Win32 calls and sends no input to do so.
3. The verifier required the `user-gesture` focus trigger on the two mouse
   focus changes. Blink records `script` for them, as described under what
   the verifier requires. The recording was re-verified with the corrected
   expectation before this run.
