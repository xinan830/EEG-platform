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

Offline high-pass, low-pass, notch, algorithms, and reports remain Python-owned
and are outside this first review slice. This avoids quietly adding a second C#
scientific filter implementation.

### Review is immersive but returns to project context

The project recording row owns the `回溯` entry action. `MainWindow` hides the
global navigation while review is open, as it does for acquisition. The review
back action disposes its session and returns to the same selected project and
refreshed recording list.

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
at most the requested visible window plus a bounded read-ahead window. Rendering
may perform extrema-preserving display decimation, but disk data and montage
calculation preserve original sample order and values.

