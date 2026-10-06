# UI Automation through the native client (agreed, built, not yet confirmed on the target machine)

Proposed 2026-10-06, after the recorder closed when a recording was stopped
on the target machine. The owner agreed to move the whole collector to the
native client the same day.

## What happened

On 2026-10-06 at 10:36:56 local time, about 16 seconds into recording
`20261006-143640-378ee39f96df4f869d8aa2b2b8dc1306`, the owner selected Stop
Recording and the recorder closed. The Windows Application log holds the
cause, from the .NET runtime:

```
System.ArgumentNullException: Value cannot be null. (Parameter 'runtimeId')
   at System.ArgumentNullException.Throw(String paramName)
   at MS.Internal.Automation.UiaCoreApi.GetUiaEventArgs(IntPtr argsAddr)
   at MS.Internal.Automation.EventListenerClientSide.OnEvent(IntPtr argsAddr, Object[,] requestedData, String treeStructure)
```

The process was ended for an unhandled exception. The recording's manifest
was left with status `recording`, so the recorder's next start marks it
interrupted. The recording before it on the same build, 4 minutes 14 seconds
long, stopped and completed.

## Why

The UI Automation collector (`UiAutomationCollector`) subscribes through the
managed client, `System.Windows.Automation`. When UI Automation delivers an
event, the managed client reads the event's arguments in
`UiaCoreApi.GetUiaEventArgs` before calling any handler of the recorder. The
stack shows that this read threw because the event's runtime ID was null.
Of the events the collector subscribes to, only `StructureChangedEvent`
carries a runtime ID in its arguments (`WindowClosedEvent` also does, and is
not subscribed to), and Microsoft's documentation of its arguments notes
that a custom control may not give a meaningful runtime ID
([StructureChangedEventArgs constructor](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.structurechangedeventargs.-ctor)).
The exception is thrown in the managed client's own callback from UI
Automation, outside every handler of the recorder, so the recorder cannot
catch it, and .NET ends the process.

Which element raised the event is not recorded, because the event never
reached the recorder. Stopping a recording changes the recorder's own window,
which may be when such an event is raised, but the evidence does not show
that; the same fault can occur at any time in a recording.

The recorder's implementation stack already names the UI Automation COM APIs
for accessibility events ([Recommended Windows Implementation
Stack](windows-implementation-stack.md)). The native client's handler for
structure changes receives the runtime ID as a `SAFEARRAY` argument
([IUIAutomationStructureChangedEventHandler::HandleStructureChangedEvent](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationstructurechangedeventhandler-handlestructurechangedevent)),
so a null runtime ID reaches the recorder's handler as a null array, and the
recorder can record it as null; the payload already allows a null
`runtimeId`.

## Change

The collector subscribes through the native UI Automation client, the COM
interface `IUIAutomation`, instead of `System.Windows.Automation`:

- the same events (focus changed, invoked, element selected, text changed,
  structure changed, and the same property changes), on the same root and
  scope;
- the same cached properties, through an `IUIAutomationCacheRequest`;
- the same `accessibility.uia.events` payloads, with a null runtime ID
  recorded as null;
- every handler's body inside the recorder's own exception handling, so a
  fault in reading one event is recorded as a collector issue and the
  collector keeps running, as a gone element already is.

The COM interfaces come from the UI Automation client type library through a
published interop assembly. The alternative, which changes less, is to move
only the structure-changed subscription to the native client and keep the
others on the managed client; it leaves the other event types read by the
managed client, which has the same failure mode if any of their argument
reads throws.

## Required tests

- Unit tests: a structure-changed observation with a null runtime ID is
  recorded with a null `runtimeId` and validates; a handler that throws is
  recorded as a collector issue and later events are still recorded.
- Integration tests on Windows: the collector subscribes, receives a focus
  change and a structure change from a test window, and unsubscribes on
  stop.
- System test on the target machine: several recordings, each stopped from
  the recorder's window, complete; the payloads of a recording match those
  of the managed client for the same actions in kind and fields.

## As built

- The COM interfaces come from the `Interop.UIAutomationClient` package,
  version 10.19041.0, an MIT-licensed import of the Windows 10 2004 UI
  Automation client type library
  ([UIAutomation-Interop](https://github.com/Roemer/UIAutomation-Interop)).
  The collector no longer references WPF.
- The collector creates `CUIAutomation8`, subscribes on the root element with
  one cache request, and removes its handlers with `RemoveAllEventHandlers`,
  which waits for running handlers to return.
- The native client gives identifiers, control types, structure-change types,
  and toggle and expand-collapse states as numbers. `UiaEvidenceText` names
  them with the programmatic names the managed client gave. Those names were
  read on the target machine from `System.Windows.Automation` on 2026-10-06.
  The managed client gave no name for control types above 50038 (semantic
  zoom and app bar), and neither does the collector.
- Property values are read with `GetCachedPropertyValueEx` and, for a sender
  delivered without cached properties, `GetCurrentPropertyValueEx`, each
  asking for a property's default value where it is not supported, as the
  managed client's reads did. A failed read is flagged by its result code:
  an element that is not available or whose process has gone is
  `element-not-available`, an invalid operation is
  `element-property-read-failed`, and any other is `uia-provider-error`.
- A fault in the recorder's handling of an event is counted, and the count is
  recorded once when the collector stops, as a `collector-omission` with
  reason `uia-event-handler-failed`, flagged `evidence-dropped`, and the
  collector's health becomes degraded. It is not recorded at the time of the
  fault, so that the records stay in time order behind the observation queue.
- The application session verifier reports how many structure changes were
  recorded and how many had no runtime ID.

### First run on the target machine

At `3b07ab3`, on 2026-10-06, recording
`20261006-162006-73ac3c88d7b64ce99f063cbbb1806e38`, 16 seconds of the timer
origin fixture, was stopped from the recorder's window and completed. The
Windows Application log holds no fault of the recorder after the one that
led to this change. That is one stop; the application session validation
has not been run at this revision.

Tests: `UiaNativeClientTests` (the names of every subscribed event and
property, control types, structure-change types and state values, the
reading of property values and rectangles, the failure flags, and a handler
fault that is counted while later events are still handled), and three
validator tests (a structure change with and without a runtime ID, and the
handler-fault omission). The Windows integration and system tests are the
application session validation, `scripts/Run-AppSessionValidation.ps1`,
which starts and stops a recording from the recorder's window under UI
Automation load, and recordings stopped from the recorder's window by hand.

## Application session validation

The first runs of the application session validation with the native client,
on the target machine on 2026-10-06, stopped before the evidence checks on
problems outside the client:

- At 3b07ab3, two managed tests failed: a recording file cut between
  two snapshot chunks of one sweep could not be read (see
  [page recreation](page-recreation.md)), and the evidence samples lacked the
  resources and compositor omissions. Both were fixed at 36dd057, and the
  managed tests then passed, 1,218 of 1,218.
- At 36dd057, the recording completed, but its file's chunk index was
  refused by the database (see
  [change-driven recording](change-driven-recording.md)). Fixed by migration
  0016 at df4911f.
- At df4911f, the script pressed Start while it was still disabled, because
  the recorder's database had not yet started; that start applied migration
  0016. The script now waits up to 180 seconds for Start to be enabled, and
  otherwise reports the app's status.
- At c7dc210, the recording 20261006-194432-e1acf87e2d6949d397621fece68ae4b9
  was started and stopped from the recorder's window under the load
  source's UI Automation load, and the app reported "Recording completed and
  stored." with no database problem. The script then stopped, because it
  expects the status of the retired session-file check, "Recording completed
  and session files verified.". The evidence checks after it read
  `events.ndjson` and `diagnostics/archive-validation.json`, which recordings
  have not had since the database store (see
  [session database](session-database.md), where the scripts are listed as
  not yet ported). The validation therefore cannot pass until the scripts
  read the recording file.
- Read from that recording's file in the sandbox: 44,544 messages, of which
  30,628 are UI Automation events (29,680 property changes, 915 structure
  changes, 17 focus changes, 16 automation events) and no collector
  omission. Two structure changes, a ChildrenInvalidated on the taskbar and
  a ChildAdded on a XAML progress ring, carry an empty runtime ID. Both were
  recorded; the managed client's fault was an event delivered without a
  runtime ID.

Decision, 2026-10-06: the validation scripts stay unported for now. The
native client's result on the target machine is the recordings stopped from
the recorder's window by hand and the c7dc210 run above, a recording started
and stopped from the window under UI Automation load and read from its file
in the sandbox.
