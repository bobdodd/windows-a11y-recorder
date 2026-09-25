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
   them.
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
- Focus changes by user gesture: by mouse to the button and the text field,
  and forward to the second button.
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

Not yet run on Windows.
