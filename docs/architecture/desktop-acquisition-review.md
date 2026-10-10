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

The WPF client is the product owner for the Windows acquisition, review, and
offline official-algorithm workspace. The Vue application is retained as a
validation and contract-inspection surface only. WPF obtains the backend
algorithm catalog and submits completed recordings through HTTP; it never
duplicates scientific formulas or writes backend persistence directly.

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
never rewrite this raw source. A recorded montage is an immutable provenance
snapshot. WPF review defaults to that snapshot; switching a compatible display
montage never replaces historical provenance or the analysis source.

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

For exact causal review filtering, the desktop may additionally persist a
disposable checkpoint-anchor cache outside the immutable recording. Anchor keys
include recording/manifest identity, sampling rate, ordered V source schema,
filter settings, Python contract/checkpoint version, contiguous-segment origin,
and next sample counter. A cold distant seek still consumes every preceding
sample in its contiguous segment once; it does not use a short warm-up as a
false causal shortcut. Intermediate history advances the Python filter in
bounded batches without creating display chunks, and completed anchors are
published atomically. Gaps terminate the old chain and start a new one.

Foreground targets are latest-only and retain the last complete frame until the
new exact frame is ready. Idle prefetch is bounded and yields whenever a
foreground target or playback load is active. Anchor files are rebuildable,
bounded, and never written into the raw recording directory.

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

The navigator thumb selects the actual `PositionSeconds` over the full
recording range, including both endpoints. It is not a viewport-start scrollbar.
The waveform follows with `start = clamp(position - visible/2, 0, duration-visible)`:
the playhead moves left-to-center at the start, stays centered while the window
pans, then moves center-to-right as the window reaches the end. Short recordings
stay wholly in view. Reverse navigation uses the same policy.

`ReviewPlaybackCursor` owns only the neutral dashed line and top triangle.
Its geometry comes from SciChart's real plot area, not the surrounding host or
axis label area. No animation clock or extra scientific state is introduced.
See [centered playhead acceptance](../validation/2026-10-10-review-playhead-report.md).

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

### Shared time display

Review and algorithm details share the workspace's in-memory
`RecordingTimeDisplaySettings`, defaulting to recording-relative seconds.
`RecordingTimeDisplayContext` combines that preference with one recording's
explicit UTC origin. Review gets its origin from the immutable manifest;
algorithm pages get the same typed timestamp through `ProjectRecordingRow`.
Never parse `StartedAtText` or substitute a registration/file creation time.

`RecordingTimeSelector` is the common segmented control. SciChart time charts
opt into `RecordingTimeAxisPresentation.SetAxis(this, xAxis)` and inherit the
detail page's presentation context. Mixed charts detach the time axis before
restoring frequency/category labels. New time charts reuse this adapter rather
than formatting timestamps independently. Review uses recording-aligned second
ticks in both modes; switching changes labels, not grid coordinates. Acquisition
clock axis labels include milliseconds (`HH:mm:ss.fff`), preserving the origin's
fractional second at every recording-second tick. Live acquisition's wall-clock
tick policy is unchanged.

Switching labels never seeks, stops playback, changes Run identity, filters,
cache keys, raw data or request `time.start_s/end_s`. Analysis inputs explicitly
remain recording seconds. Cursors show both bases when the origin is known;
clock labels include the date after midnight. Missing origins disable clock
selection. Durations and scientific diagnostic evidence retain explicit seconds.
The session preference is not persisted across application restarts.

[Time display validation and acceptance](../validation/2026-10-10-recording-time-display-report.md)
records automated coverage and the separate manual GPU/window checks.

## 8. Compatibility and provenance

Channel configuration describes available source channels. A montage describes
how compatible source channels are displayed or mathematically combined. A
recording stores snapshots and versions, so later edits cannot silently change
the meaning of an existing recording.

Before acquisition, resolve the actual device stream, channel table, sampling
rate, and selected montage. Persist the complete selected montage snapshot in
the manifest and mark its profile ID as historically referenced. Persisted
montage profiles become immutable once referenced: subsequent edits use
“复制为新配置”, and stale drafts cannot overwrite a referenced ID. Unused user
profiles remain editable. Deleted, archived or modified preview presets cannot
start a recording with stale metadata. The review page defaults to the validated
acquisition snapshot; if it is missing or invalid, it explicitly falls back to
raw channels. Alternate display montages still require the same channel-profile
ID and valid source/reference mappings. Locking a preset is not a replacement
for scientific compatibility checks and does not remove review rereferencing.

Historical edit locks and current deletion locks have different lifetimes.
`MontageRecordingReferences` verifies project recordings, recoverable
`.trash/recordings`, retained project roots and registered external locations.
Existing references prohibit deletion; after the last discoverable reference is
permanently removed and verification succeeds, the user preset can be deleted,
even when archived. Its historical ID cannot subsequently be overwritten or
reused. Normal project deletion moves recordings to trash and therefore does not
release this protection. No automatic purge or preset deletion is performed.

The adjacent version-2 reference index persists historical IDs, unlocated IDs,
roots and recording directories. Unknown/no-path registrations remain protected.
Malformed indices/manifests, inaccessible disks or raw files without their
registered manifest fail verification rather than silently unlocking. Publication
and preset mutations share one in-process gate, covering the pre-manifest gap.
Migration of legacy ID arrays scans known projects/trash; it cannot discover old
unregistered external copies. Moving a recording outside known roots requires
opening/registering its new location. Independent concurrent desktop processes
are not supported; older builds cannot read the version-2 index. Restart into the
new build and do not delete the index to bypass protection.

See [reference-release acceptance](../validation/2026-10-10-montage-reference-release-report.md).

Device-session status notifications do not represent a new physical device.
The acquisition workspace compares selected device facts with the last facts
applied to setup using `DeviceSessionDiscoveryComparer`. An equivalent device
must not reload legacy/default labels or discard the selected montage's channel
snapshot during stream setup, preview, recording, pause, resume or stop. A
confirmed disconnection or actual device/capability change still reloads setup.
For a snapshot declaring CH3=F88 and CH5=F3, live F88-REF reads CH3 and live
F3-REF reads CH5; labels are not resolved through an earlier device template.

The synthetic regression and hardware acceptance instructions are recorded in
[the live mapping validation report](../validation/2026-10-10-live-acquisition-channel-mapping-report.md).

### Montage output appearance

Montage outputs own optional opaque RGB `Color` and `DisplayOrder`. These are
presentation fields, not native source indexes. `MontageOutputOrder` moves draft
outputs and renumbers their display positions without changing labels, reference
dependencies or the bound channel snapshot. Full montage fingerprints include
explicit colors; legacy no-color fingerprints and strict channel-profile-ID
compatibility remain unchanged.

`Shared/Controls/ColorPicker` owns preset swatches, HEX entry and local RGB
preview/apply/cancel. Event and montage editors bind to its `SelectedColor`
property; business modules do not implement their own picker. The montage editor
uses compact mode in `MontageOutputTable`, retaining reference selectors while
omitting the redundant negative-input-number column. System presets remain
read-only until copied.

Acquisition persists these fields in the existing immutable montage snapshot.
Live traces and review frames carry the selected snapshot's colors to SciChart;
the renderer refreshes Stroke even when output names remain unchanged. Review
defaults to the acquisition snapshot, not the current preset. Outputs without
explicit colors use the shared eight-color `WaveformTraceColors` palette; hidden
montage outputs do not renumber that palette. Editing/saving a legacy preset
materializes its fallback colors so subsequent reordering preserves them.

The raw writer, physical channel mapping, reference formulas, filtering and
scientific analysis are unchanged. Analysis choices continue to use saved output
order; a color is never an input to the EEG calculation. Acceptance evidence and
manual device checks are in
[the montage appearance report](../validation/2026-10-10-montage-appearance-report.md).

### Analysis source selection

For completed WPF recordings, the desktop registers an analysis view with
`POST /api/recordings/register-wpf` and `signal_mode=acquisition_montage`.
Its channel choices are the acquisition montage's saved outputs, in saved
display order, not every raw EEG input. For example, a recording acquired with
`F7-REF` and `F3-REF` exposes exactly those two analysis choices even when the
device wrote more raw channels.

`acquisition_analysis_signals.py` verifies the immutable channel/montage
snapshots against actual native inputs, labels, kinds, V units and hardware
reference metadata. Its bounded reader projects REF, bipolar, average and
specified-pair signals before continuous preprocessing. NaN gaps survive;
missing or conflicting metadata returns `ACQUISITION_MONTAGE_UNVERIFIABLE`
instead of guessing or silently exposing raw inputs.

Review display-montage changes do not alter this analysis view. Runs persist
source formulas, mapping, snapshot digest and reference mode; the source view
also participates in cache identity. Raw files and historical raw-mode Runs
remain unchanged. API callers omitting `signal_mode` retain legacy raw mode;
the desktop does not currently expose an advanced raw-mode selector.

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
