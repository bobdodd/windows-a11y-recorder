# Chromium Recorder Bridge

This directory is copied into the Chromium source checkout as
`//chromium/recorder_bridge`. It mirrors version `0.2` of the recorder-side
protocol implemented by `Recorder.Collectors.Browser`.

Run the integration and build from a Windows PowerShell prompt:

```powershell
.\chromium\setup-windows.ps1
```

The integration script copies this directory into the checkout, adds it to the
`chrome_main_delegate` and Windows `content/browser` targets, and initializes
the connection from `ChromeMainDelegate::BasicStartupComplete`. The browser
process completes its recorder connection before normal startup can launch
renderer processes. Child bootstrap attachment runs in Chromium's shared
`LaunchOnLauncherThread` sequence immediately before platform-specific launch
preparation. The integration script removes the obsolete Windows-local hook
when updating an existing checkout. The instrumented browser:

1. Recognizes `--a11y-recorder-bootstrap=stdin`.
2. Reads the one-line secret bootstrap from inherited standard input.
3. Authenticates to the recorder and completes clock synchronization.
4. Copies the child bootstrap into a browser-owned read-only shared-memory
   region. It publishes only Chromium's opaque serialized handle metadata to
   the browser process environment so the child-launch hook can cross linked
   module boundaries without relying on duplicated static storage.
5. Starts renderer, GPU, and utility process bridge connections using the
   inherited capability. Their command lines contain only shared-memory handle
   metadata and the non-secret Chromium child process identifier. The internal
   metadata environment marker is removed from each child environment.
6. Authenticates and synchronizes each participating process independently.
7. Must route browser and child-process evidence through bounded, non-blocking
   queues to `RecorderPipeClient`.
8. Must report queue overflow and disconnected intervals as omission records.

The current code implements and integrates the browser-process bootstrap,
child-process capability distribution, per-process authentication and clock
synchronization, framing, and evidence serialization. Blink evidence hooks are
the next implementation slice.

For an explicit local diagnostic run, set
`A11Y_RECORDER_CHROMIUM_LOG_FILE` to an absolute file path before starting the
capture host. The launcher then adds Chromium's `--enable-logging` and
`--log-file` switches. This option is disabled by default because Chromium logs
can contain tested URLs and other local browsing details. Diagnostic logs are
not session evidence and must not contain the recorder authentication token.

For failures that occur before Chromium logging is initialized, set
`A11Y_RECORDER_BRIDGE_LOG_FILE` to an absolute file path. The native bridge
appends only process identifiers, monotonic tick values, bootstrap attachment
stages, child process types, and internal error text. It never writes bootstrap
contents, pipe names, authentication tokens, command lines, URLs, or page data.

For each accepted connection, the recorder persists two records on the
`browser.lifecycle` channel:

1. `browser-connected` after the hello message and authentication token have
   been validated.
2. `browser-clock-synchronized` after the clock exchange has completed and the
   recorder has sent the ready message.

The connection record contains protocol, browser-instance, process, and
Chromium-version metadata. Child records also contain the browser OS process ID
and Chromium child process ID. No lifecycle record contains the authentication
token. The clock record contains the mapping identifier, Chromium monotonic
frequency, and estimated uncertainty in nanoseconds. A failed authentication or
handshake produces `browser-connection-rejected` instead of either successful
lifecycle record.
