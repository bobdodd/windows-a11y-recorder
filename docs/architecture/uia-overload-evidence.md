# UI Automation overload evidence

This note records why the UI Automation collector changed how it queues observations, reads element properties, and states loss, and what those changes do and do not guarantee.

## Observed overload

The collector subscribes to focus changes, automation events, structure changes, and property changes for the whole desktop. Any application on the desktop contributes to that stream, not only the application under test.

Six application-launched validation sessions recorded on 2026-09-25 show the scale:

- Five sessions without loss recorded 8,639 to 9,141 UI Automation observations each. Property changes were 7,852 to 8,310 of them, structure changes 746 to 797, automation events 20 to 24, and focus changes 14 to 17. The busiest second in each session held 1,251 to 1,352 observations.
- In the last of those five, 6,630 observations were name property changes on XAML `Text` elements in process 17100, Microsoft Solitaire, which was open on the desktop but not part of the test.
- The sixth session recorded 7,799 such Solitaire name changes, and its rate reached 1,588 observations per second. The 4,096-slot observation queue filled and the collector refused 3,226 observations. The refused observations included the focus change the application session verifier requires, so that run failed.

In that sixth session, the loss was stated only as one `collector-omission` record written when collection stopped, with a total count. The archive could not show when the loss happened, what kinds of observation were lost, or whether the focus change the verifier looked for had been lost or never raised.

Two costs made the queue fill. First, every observation shared one queue, so thousands of name changes from one application could take the capacity that a rare focus change needed. Second, the processor read each element's properties after dequeuing, with one cross-process call per property. At more than 1,000 observations per second, that reading fell behind arrival.

## Changes

### Loss stated per episode

A drop episode is a run of refused observations with no admitted observation between them. The collector now writes one `collector-omission` record for each episode, with reason `uia-observation-queue-full`. The record states:

- `count`: the number of observations refused in the episode.
- `firstDroppedAtNanoseconds` and `lastDroppedAtNanoseconds`: the monotonic arrival times of the first and last refused observations.
- `droppedByObservationType`: the count for each observation type refused, which sums to `count`.

The record is timed at the last refused arrival. It enters the queue immediately before the next admitted observation, so the archive keeps arrival order. If the queue has no room for both the record and the next observation, the observation is refused too and the episode continues. An episode still open when collection stops is written then. If the queue stays full for 5 seconds at stop, the collector writes the episode directly after the processor finishes.

Archives written before this change carry one queue-full omission with only `reason` and `count`. The validator accepts that form. When any episode field is present, the validator requires all of them, requires the first time not to follow the last, requires the record to be timed at the last, and requires the per-type counts to be positive and to sum to `count`.

### Reserved capacity for focus and automation events

The queue keeps 512 of its 4,096 slots for focus changes and automation events (invoke, selection, and text changed). Property and structure changes may use only the other 3,584. The five sessions without loss held 34 to 41 focus changes and automation events each, so the reserve is an order of magnitude above observed use.

The reserve is one queue with a limit per kind rather than a separate queue per kind. The validator requires monotonic time not to regress within a collector channel, and all four kinds share the `accessibility.uia.events` channel. The queue reads the arrival time and decides admission under one lock, so observations leave it in arrival order.

The reserve does not guarantee that a focus change is kept. Focus changes and automation events together can still fill it, and the processor can still fall behind. A refused focus change appears in `droppedByObservationType` under `focus-changed`, and the application session verifier reports that count when it fails to find a required focus change.

### Element properties read with the event

The collector now subscribes while a UI Automation cache request is active. The request asks for the element itself (`TreeScope.Element`), in full mode, with the thirteen properties each observation records: process ID, native window handle, automation ID, name, class name, framework ID, control type, localized control type, keyboard focus, keyboard focusability, enabled state, offscreen state, and bounding rectangle.

Microsoft's documentation states that "caching also occurs when you subscribe to an event while a CacheRequest is active. The AutomationElement passed to your event handler as the source of an event contains the cached properties" ([Caching in UI Automation Clients](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/caching-in-ui-automation-clients)). The processor reads those cached values instead of making one cross-process call per property.

The collector contract requires the event-handler path to avoid synchronous tree traversal and property expansion. That requirement still holds. UI Automation fetches the cached properties before it calls the handler, and the handler only records the arrival time and queues the sender.

Each element snapshot now states `propertySource`:

- `event-cache`: the values were cached when UI Automation raised the event.
- `current-read`: the sender arrived without them, so the processor read the current values after dequeuing, and they may postdate the event. The read's failures remain in `qualityFlags` as before.

Snapshots in archives written before this change state no `propertySource`. Their values were read after dequeuing.

## Validation under load

Two application-launched validation runs at this change passed with no drop episodes, and every one of their 1,193 and 1,194 UI Automation observations stated `event-cache`. Solitaire was running during both but raised no UI Automation events, so neither run measured the collector under load. Whether an application floods UI Automation depends on its state, which a validation run cannot control.

The application session validation therefore starts its own load source, `tests/UiaLoadSource`, before the recorder app and closes it after the recording stops. It is a small window of 40 text elements that raises name changes on them at a set rate, 2,000 per second by default, only while a UI Automation client listens for property changes. The rate and element type imitate the application that overflowed the queue. When it closes, it writes how many changes it raised, over what span, and its busiest second.

The run fails if the source's mean rate is below 1,500 per second, so a run that did not apply the load cannot pass as one that did. The verifier reports how many observations the recorder received from the source's process and the busiest second of arrival. The existing focus checks decide whether focus evidence survived the load. Drop episodes are reported, not failed, because recording loss under load and stating it is the designed behavior.

## Limits

- Whether caching reduces the processing time per observation enough to prevent overflow at the observed rates has not been measured. The first validation run with the load source is the first measurement.
- The load source raises events from one WPF process. A native or XAML provider, or several sources at once, may load UI Automation differently.
- Loss inside UI Automation itself, before an event reaches the collector, is not visible to the collector and is not stated.
- The collector still observes the whole desktop. Limiting it to the process under test would reduce load but would also stop recording evidence from other processes, which the recorder records by design.
