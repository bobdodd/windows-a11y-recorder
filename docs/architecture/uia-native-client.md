# UI Automation through the native client (proposed, not built)

Proposed 2026-10-06, after the recorder closed when a recording was stopped
on the target machine.

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
