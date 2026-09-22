## Context

Each completed desktop session owns a `manifest.json`, one or more
`samples-*.bin` chunks, and an `audit.jsonl`. Chunk values are sample-major
float64. Each batch begins with first sample counter, sample count, channel
count, and PC receive ticks. The manifest contains the actual stream channel
table and, for current recordings, serialized channel-configuration and montage
snapshots inside `HardwareConfiguration`.

The project page already discovers recording directories through
`LocalRecordingCatalog`. The live waveform path is not an appropriate review
reader: it owns device/session lifecycle and a bounded recent-history buffer,
while review needs deterministic random windows over immutable disk data.

## Decisions

### Review owns a read-only indexed session

`LocalRawRecordingReader` parses the manifest and scans chunk headers into a
small in-memory index. It does not cache the complete sample payload. Window
reads seek only to intersecting batch payloads and return sample-counter-based
segments. Explicit gaps remain gaps; the reader never inserts zero or
interpolated EEG values.

Elapsed recording time is derived from `(counter - firstCounter) / sfreq`.
`recording_start_utc` anchors that relative time. PC receive ticks remain
diagnostic metadata only.

### Acquisition and viewing montages remain separate

The embedded acquisition montage snapshot is the default review montage and is
shown even if the current global profile was later edited or deleted. The
workspace also loads current saved montage profiles, but exposes only profiles
whose required positive and negative labels resolve uniquely to signal columns
in this recording and whose device/channel contract is compatible.

The page shows both:

- `采集导联`: immutable snapshot recorded with the session.
- `当前查看导联`: mutable review-session choice.

Changing the viewing montage rebuilds derived traces from the same raw window.
The absolute playback position and visible duration do not change. The number
of output traces is defined by `DerivedChannels.Count`, so 20-to-13 and
13-to-20 switches are ordinary state transitions.

### Display derivation is shared, raw storage is not

Montage source resolution and `A`, `A-B`, and `A-Mean(...)` projection move to
a focused display-domain service shared by live and review frame builders. The
service accepts V/float64 values and returns V/float64 derived display values.
It never mutates its source arrays.

Review high-pass, low-pass, and notch remain Python-owned. Review uses a
rebuildable fixed-size **filtered source chunk** cache: each completed chunk
contains only Python-filtered V/float64 values in the recording's original
device stream order. It deliberately does not contain a display montage, so a
review-only montage switch can reuse the filtered source data. C# owns only
immutable raw-window reads, cache persistence, playback orchestration,
sensitivity, paper-speed geometry, and display montage projection.

A chunk key includes the immutable recording session and raw-manifest
fingerprint, sampling rate, recorded channel schema fingerprint, filter
settings, Python-returned filter-contract fingerprint, filter warm-up/checkpoint
contract, chunk sample range, and gap boundary context. It MUST NOT include a
later-edited global channel configuration version. A random noninitial chunk is
built with contiguous preceding raw samples sufficient to establish its causal
filter state; recorded gaps break that state chain. A partially written cache
file is never readable: write a complete temporary file and atomically rename
it only after Python returns all target samples.

The C# review path has a small montage/frame LRU above the filtered source
cache. SciChart receives only a complete projected frame. On a cache miss the
previous complete frame remains visible while a background chunk builds; no
half-filled data series, empty target range, or partially filtered samples are
bound to the chart. A filter failure retains raw display data with an explicit
warning; it never fabricates filtered values or adds a second C# scientific
filter implementation.

### Review is immersive but returns to project context

The project recording row owns the `回溯` entry action. `MainWindow` hides the
global navigation while review is open, as it does for acquisition. The review
back action disposes its session and returns to the same selected project and
refreshed recording list.

### Review navigation uses a full-recording time index

The bottom navigator represents only the complete recorded time range and the
current bounded display window. It does not plot or cache the full raw record.
Seeking moves the chart's continuous absolute-time visible range immediately.
Raw reads and Python filtering are debounced, latest-only background work; an
in-memory cache retains only a few nearby raw and projected blocks for the
active configuration. Playback pans through those blocks and prefetches the
next one. The horizontal
EEG grid and navigator labels are derived from `recording_start_utc` plus
sample-counter time. They are historical acquisition times, never the current
workstation clock.

Paper speed is the only horizontal display control. The view calculates visible
seconds from WPF's device-independent viewport width and the selected mm/s;
clinical workstations that require physical-mm fidelity still need a monitor
calibration step because a DIP is not a guaranteed physical millimeter.

### Display scale context

Live acquisition and recording review resolve an immutable display-scale
context from the window that owns the waveform. The context is refreshed when
that window is loaded, resized, or moved between monitors. Horizontal paper
speed uses the calibrated display width; vertical EEG sensitivity uses the
calibrated display height. A calibration on one window or monitor must not
mutate the scale of another open acquisition or review window.

## Failure Behavior

- Missing or malformed manifest/chunk/audit data produces a readable load
  failure; no synthetic waveform is displayed.
- A missing historical montage snapshot is labeled explicitly. The raw device
  signal view remains available, but it is not presented as the unknown
  acquisition montage.
- A saved montage with absent, duplicate, non-signal, or wrong-unit source
  labels is excluded with a compatibility reason.
- Truncated chunk payloads fail the affected read with file, offset, expected
  bytes, and actual bytes in diagnostics.
- A montage switch requested during an older in-flight read cancels or discards
  that result so stale traces cannot replace the newer selection.

## Performance Boundary

Opening a recording scans only headers and seeks over payloads. Playback keeps
only a bounded set of nearby raw, filtered-source, and projected display frames,
never the complete recording. Dragging updates a preview target continuously;
the committed waveform target changes only after a matching complete frame is
available. Nearby fixed chunks are prefetched without cancelling a useful
completed or adjacent build for every pointer movement. Rendering may perform
extrema-preserving display decimation, but raw and filtered cache data preserve
original sample order, V units, and float64 values.
