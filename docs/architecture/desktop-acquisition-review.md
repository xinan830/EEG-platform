# Desktop Acquisition And Recording Review Architecture

This document is the canonical implementation guide for the BrainPlatform
Windows client. It explains how the desktop client, local Python services, raw
recordings, and review UI cooperate. It is deliberately more concrete than a
product overview and more stable than a list of individual bug fixes.

## 1. Ownership model

The system has one source of truth for each responsibility:

| Responsibility | Owner | Must not be duplicated by |
| --- | --- | --- |
| Device identity, connection, capabilities, stream handle | `DeviceSessionManager` | individual pages or dialogs |
| SDK reads, batch queue, drop diagnostics, raw persistence | C# acquisition runtime | WPF dispatcher or Python |
| Scientific filters, montage mathematics, PSD, algorithms | Python backend | C# display code |
| Current live display frame | C# display pipeline | raw writer or SDK callback |
| Review window, derived chunks, playback target | C# review session/cache | chart control or Python request caller |
| Analysis provenance and scientific artifacts | backend persistence | desktop SQLite writes |

The UI binds to state owned by these services. A page must not enumerate a
device, read a recording format, or infer scientific state as a side effect of
rendering.

## 2. Device and acquisition state

Keep these states separate:

```text
Disconnected -> Detecting -> Connected -> Ready -> Streaming -> DeviceLost
                                             \-> Error

Streaming + recording stopped/paused/started are independent recording states.
```

Opening the acquisition preparation page may prepare a stream, but it must not
start a recording. The bottom play/pause control starts or pauses recording;
the persistent stop control finalizes the recording. Pausing recording must not
freeze the live waveform or stop the device stream.

Device discovery follows this policy:

```text
Windows device event -> debounce 0.5-1 s -> SDK confirmation -> manager update
```

Use a lightweight 15-30 second idle confirmation as a fallback and keep manual
refresh. Do not perform full SDK enumeration in the stream loop. A failed read,
timeout, callback failure, or invalid handle during streaming is the immediate
device-loss signal. A periodic status check must never open a device picker.

## 3. Acquisition data path

The SDK producer emits sample-major batches. The runtime places bounded batches
into a queue/ring buffer; the raw writer, display buffer, and analysis bridge
consume them independently:

```text
vendor SDK
  -> serialized stream owner
  -> bounded sample batches
       |-> immutable raw chunks + manifest + audit
       |-> bounded live display buffer
       \-> bounded Python display-filter bridge
```

The SDK's actual opened stream defines channel order, channel type, unit,
sampling rate, and sample-counter position. Do not derive channel count from a
montage or configured screen channels. EEG values are `double` in `V`; sample
counter values are `count`; convert units exactly once at the display boundary.

The raw writer must not depend on the WPF dispatcher or Python availability.
The UI must not send one request per sample, notify one property per sample, or
append one point per SDK callback. Render batches at a bounded cadence. A high
sample rate increases batch size/throughput, not UI callback frequency.

Persist counter gaps, queue overflow, read failures, and reconnects as audit
facts. Never insert fabricated samples to make a waveform look continuous.
PC receive time is transport diagnostics; scientific time is the validated
sample counter divided by sampling rate and anchored to the recording start.

## 4. Raw recording contract

Raw recordings are immutable and are the only scientific source of truth. A
recording contains, at minimum:

- stable recording/session and project identity;
- recording start time and sampling rate;
- actual channel table, stream indices, kinds, labels, and units;
- selected device/model/SDK metadata;
- sample-major float64 raw chunks;
- sample-counter and gap/drop audit information;
- acquisition channel/montage snapshots used at recording time.

Display filtering, sensitivity, paper speed, and review montage changes must
never rewrite this raw source. A recorded montage is a provenance snapshot, not
a promise that later review cannot select another compatible display montage.

## 5. Python boundary

Python owns scientific filtering and derived analysis. The desktop sends bounded
windows/batches, never individual samples, and receives a contract that states
sampling rate, actual range, output unit, quality, and gap/drop context.

When live filter settings change, keep the old complete display frame visible
until the new complete frame is available. Publish the new frame atomically.
Never clear a SciChart series and expose a partially rebuilt series. Never
reimplement an IIR filter in C# as a performance workaround.

The current fixed warm-up cap is not a strict causal-state guarantee for a
`0.01 Hz` high-pass. Full continuity requires Python checkpoint/state transfer
or an equivalent validated contract; until then this is a documented limit.

## 6. Review pipeline

Review must not load a complete long recording or rebuild a filter on every
pointer movement. The implemented data path is:

```text
immutable raw recording
  -> fixed 10-second filtered source chunks
  -> display-window/frame cache
  -> compatible montage projection
  -> bounded chart data
  -> SciChart
```

Filtered chunk fingerprints include recording/manifest identity, source channel
schema, sampling rate, filter settings, Python contract/version, sample range,
warm-up semantics, and processing version. Derived cache files are disposable;
write a temporary file and atomically rename it only after completion. A
partial or stale chunk must never be bound to the chart.

Use latest-only scheduling for drag requests, deduplicate identical in-flight
chunk builds, retain a small frame LRU, and prefetch nearby chunks. A review
frame is committed only after its complete target data is ready. Keep the prior
frame visible during a target build.

## 7. Navigator and playback

The timeline has two intentionally different paths:

### Thumb drag

Update an accepted target continuously, but cap movement to a bounded rate. The
reference behavior uses two visible pages per second. If the pointer requests
more than the accepted target, synchronize the OS pointer back to the accepted
thumb location. Do not add an autonomous slow animation that chases the
pointer; it creates pointer/thumb divergence and stale waveform ranges.

### Track click

Intercept the click before changing the visible range. Load the target frame,
then atomically commit viewport, playback position, and frame. Do not move the
visible thumb to an unloaded target or show an empty transient series.

Playback has a high-frequency clock independent of loading. It activates a
cached frame immediately and starts one bounded foreground load when crossing a
cache edge; it must not reread the visible range on every render tick.

Review time remains sample-counter based. Preserve explicit recording gaps.
The operating-system clock may label recording wall time and diagnose latency,
but it is not a replacement for EEG sample time.

## 8. Compatibility and provenance

Channel configuration describes available source channels. A montage describes
how compatible source channels are displayed or mathematically combined. A
recording stores snapshots and versions, so later edits cannot silently change
the meaning of an existing recording.

Before acquisition, resolve the actual device stream, channel table, sampling
rate, and selected montage. At review time, only show montages compatible with
the recorded source schema. Switching from a 20-output montage to a 13-output
montage changes display projection, not the raw channel count.

## 9. Verification checklist

For desktop acquisition changes:

1. Run the full desktop test suite and Release build.
2. Test actual channel order, units, sample counter continuity, sustained high
   sample-rate capture, queue/drop audit, stop, disconnect, reconnect, and
   process restart on the target amplifier.
3. Independently parse a completed raw recording from its manifest.

For review changes:

1. Test initial load, cache hit, cache miss, filter invalidation, montage output
   count changes, corrupted/incomplete cache files, recorded gaps, playback,
   thumb drag, and track click.
2. Verify that old frames remain visible until a complete target frame exists.
3. Run OpenSpec strict validation and `git diff --check`.

Passing unit tests is evidence for contracts, not evidence that a physical
amplifier or GPU has been accepted.
