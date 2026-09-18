# Chromium Recorder Bridge

This directory is copied into the Chromium source checkout as
`//chromium/recorder_bridge`. It mirrors version `0.1` of the recorder-side
protocol implemented by `Recorder.Collectors.Browser`.

Run the integration and build from a Windows PowerShell prompt:

```powershell
.\chromium\setup-windows.ps1
```

The integration script copies this directory into the checkout, adds it to the
`chrome_main_delegate` target, and initializes the connection from
`ChromeMainDelegate::BasicStartupComplete`. That occurs before normal browser
startup can launch renderer processes. The instrumented browser:

1. Recognizes `--a11y-recorder-bootstrap=stdin`.
2. Reads the one-line secret bootstrap from inherited standard input.
3. Authenticates to the recorder and completes clock synchronization.
4. Must next distribute the non-secret pipe identity and a short-lived child
   capability to instrumented child processes through Chromium IPC. Do not copy
   the root authentication token into child command lines.
5. Must route browser and child-process evidence through bounded, non-blocking
   queues to `RecorderPipeClient`.
6. Must report queue overflow and disconnected intervals as omission records.

The current code implements and integrates the browser-process bootstrap,
authenticated connection, clock synchronization, framing, and evidence
serialization. Child capability distribution and Blink evidence hooks are the
next implementation slice.

For each accepted connection, the recorder persists two records on the
`browser.lifecycle` channel:

1. `browser-connected` after the hello message and authentication token have
   been validated.
2. `browser-clock-synchronized` after the clock exchange has completed and the
   recorder has sent the ready message.

The connection record contains protocol, browser-instance, process, and
Chromium-version metadata. It never contains the authentication token. The
clock record contains the mapping identifier, browser monotonic frequency, and
estimated uncertainty in nanoseconds. A failed authentication or handshake
produces `browser-connection-rejected` instead of either successful lifecycle
record.
