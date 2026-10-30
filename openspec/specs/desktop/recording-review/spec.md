# desktop/recording-review Specification

## Purpose
TBD - created by archiving change add-desktop-recording-review. Update Purpose after archive.
## Requirements
### Requirement: Project recordings open in an immersive review workspace

The desktop SHALL expose a review action for a selected recording under its
owning project. The action SHALL open a full-content review workspace without
the global navigation and SHALL retain enough project context to return to the
same project recording list.

#### Scenario: A user opens a completed project recording

- **WHEN** the user invokes `回溯` for a readable recording
- **THEN** the desktop SHALL load that exact recording directory
- **AND** it SHALL show the recording identity, absolute position, duration,
  sampling rate, and signal-channel facts from the recording
- **AND** returning SHALL restore the owning project selection.

### Requirement: Review reads immutable raw chunks by bounded time window

The desktop SHALL read the manifest channel table and sample-major float64/V
chunks without rewriting them or loading the complete recording into memory.
Review time SHALL be derived from sample counter and sampling rate. Recorded
gaps SHALL remain explicit and SHALL NOT be filled with fabricated samples.

#### Scenario: A requested window intersects a recorded gap

- **WHEN** the review reader loads a time range spanning a sample-counter gap
- **THEN** it SHALL return separate contiguous segments around the gap
- **AND** the renderer SHALL not connect those segments with a line
- **AND** the reader SHALL not insert zeros or interpolated samples.

#### Scenario: A chunk is truncated

- **WHEN** a batch header declares more payload bytes than remain in its chunk
- **THEN** review SHALL fail that read with an explicit corrupt-recording error
- **AND** it SHALL not display partial values as a complete window.

### Requirement: Review separates acquisition montage from viewing montage

The desktop SHALL default a review session to the montage snapshot stored by
the acquisition. It SHALL display that immutable acquisition montage separately
from the mutable current viewing montage. Changing the viewing montage SHALL
not change raw data or the recorded acquisition snapshot.

#### Scenario: A recording opens with a valid acquisition montage snapshot

- **WHEN** the snapshot's source labels resolve uniquely to recorded EEG signal
  columns
- **THEN** it SHALL be selected as the initial viewing montage
- **AND** both `采集导联` and `当前查看导联` SHALL show its recorded name.

#### Scenario: A legacy recording has no acquisition montage snapshot

- **WHEN** the recording lacks that snapshot
- **THEN** the desktop SHALL state that the acquisition montage is unavailable
- **AND** it SHALL NOT infer a historical montage from the current global
  configuration
- **AND** it MAY offer a clearly labeled raw device-signal view.

### Requirement: Review exposes only compatible alternative montages

A montage SHALL be review-compatible only when every positive and negative
source resolves uniquely to a recorded Reference or Bipolar signal column with
the declared EEG unit and the profile's source-channel contract is valid.
Trigger and sample-counter columns SHALL NOT satisfy montage sources.

#### Scenario: An alternative montage requires a missing source

- **WHEN** a montage requires `M2` but the recording has no uniquely mapped
  signal column labeled `M2`
- **THEN** that montage SHALL not be selectable
- **AND** its incompatibility reason SHALL identify `M2` as missing or
  ambiguous.

### Requirement: Montage switching preserves review time

The desktop SHALL derive the trace list from the current viewing montage.
Changing from a montage with one output count to another SHALL atomically
replace the complete visible trace set while preserving absolute playback
position and visible duration.

#### Scenario: A 20-output montage changes to a 13-output montage

- **WHEN** the user switches compatible montages at absolute position 125.0 s
  with a 10 s visible duration
- **THEN** the page SHALL replace 20 traces with the 13 derived traces
- **AND** it SHALL remain at 125.0 s with a 10 s visible duration
- **AND** no raw samples or acquisition metadata SHALL change.

### Requirement: Playback clock is independent from waveform window loading

The desktop SHALL advance the visible playback position from a monotonic clock
without rereading and reprojecting the complete visible window on every UI
tick. It SHALL pan a continuous visible time range inside a bounded nearby-data
cache and SHALL atomically replace cache blocks only after the target block is
complete.

#### Scenario: A 4000 Hz recording plays within one visible page

- **WHEN** the playback clock advances repeatedly before the current page end
- **THEN** the displayed position and cursor SHALL advance smoothly
- **AND** those clock ticks SHALL not trigger additional raw-window reads
- **AND** the next bounded page MAY be prefetched before the boundary.

### Requirement: Review navigates the full recording through recorded time

The review workspace SHALL use one full-recording time navigator. The main
waveform SHALL display only the bounded window at the navigator position; the
desktop SHALL NOT read all raw samples merely to render the navigator. Labels
on the waveform and navigator SHALL be derived from `recording_start_utc` plus
sample-counter time, not from the computer's current clock.

#### Scenario: A user drags the recording time navigator

- **WHEN** the user drags to an absolute recorded time
- **THEN** the desktop SHALL move the visible range immediately and continuously
- **AND** it SHALL debounce and coalesce target raw-window requests so obsolete
  drag locations do not queue disk or Python filter work
- **AND** a cache hit SHALL update the waveform without another raw read or
  Python filter call
- **AND** it SHALL update the waveform and current cursor to the matching
  recorded clock time
- **AND** it SHALL preserve the full recording start and end time as navigator bounds.

### Requirement: Review controls affect the display path only

The desktop SHALL provide high-pass, low-pass, notch, sensitivity, and paper
speed controls for review. Visible duration SHALL be derived from the waveform
viewport width and the selected paper speed; it SHALL NOT be a separate user
time-base selector. Scientific filtering SHALL be performed by the
local Python filter service over V/float64 data. Sensitivity and time base SHALL
affect only display geometry. None of these controls SHALL rewrite raw chunks.

#### Scenario: A user changes a review filter

- **WHEN** the user changes a valid high-pass, low-pass, or notch value
- **THEN** the current bounded raw page SHALL be filtered by the Python service
- **AND** a noninitial contiguous window SHALL advance the new causal filter
  from a bounded preceding raw warm-up interval before its visible samples are
  filtered
- **AND** a recorded gap SHALL break the filter-state chain rather than being
  treated as fabricated continuous signal
- **AND** the complete previous frame SHALL remain visible until its replacement is ready
- **AND** the immutable raw recording SHALL remain unchanged.

#### Scenario: The local filter service is unavailable

- **WHEN** a review page cannot reach the Python filter service
- **THEN** the desktop SHALL keep the raw display available
- **AND** it SHALL explicitly state that filtering is unavailable
- **AND** it SHALL not claim that the selected filter was applied.

### Requirement: Review caches completed filtered source chunks without changing raw data

The desktop SHALL maintain a rebuildable fixed-size derived cache outside the
immutable recording directory. A derived chunk SHALL contain Python-filtered
V/float64 source channels in their recorded stream order, not a display montage.
Its key SHALL include the recording/raw manifest identity, sampling rate,
recorded channel schema, filter settings, Python filter-contract fingerprint,
chunk sample range, and causal warm-up or checkpoint context. A global channel
configuration edited after acquisition SHALL NOT invalidate a historical
recording's source cache.

#### Scenario: A user changes only the viewing montage

- **WHEN** the filtered source chunks for the visible range already exist
- **AND** the user selects another compatible viewing montage
- **THEN** the desktop SHALL reuse those filtered chunks
- **AND** it SHALL apply only display montage projection to build the new frame
- **AND** it SHALL not call the Python filter service again for that range.

#### Scenario: A filter setting or filter contract changes

- **WHEN** a high-pass, low-pass, notch, or Python filter-contract version changes
- **THEN** the prior filtered chunk SHALL not be used as a cache hit
- **AND** the desktop SHALL build a new chunk from immutable raw data.

#### Scenario: A cache write is interrupted

- **WHEN** a filtered chunk build is cancelled, fails, or exits before all target samples are written
- **THEN** the incomplete file SHALL not be considered a readable cache entry
- **AND** a later request SHALL rebuild it from immutable raw data.

### Requirement: Review never exposes an incomplete target frame

The time navigator MAY update a preview target continuously, but the review
session SHALL retain its last complete frame object until a complete matching
frame is ready. When the requested viewport is outside that frame, the chart
axis and playhead SHALL follow the requested target and the plot MAY be empty
temporarily; it SHALL not display the old samples under the new time labels.
SciChart SHALL NOT bind a partially constructed source chunk or partial montage
frame.

#### Scenario: A user drags across an uncached chunk boundary

- **WHEN** the navigator target enters a chunk that has not finished building
- **THEN** the previous complete frame SHALL remain retained by the session
- **AND** if its time range does not cover the target, the plot SHALL show an
  explicit loading state instead of relabeling those samples
- **AND** the target waveform SHALL replace it atomically only after all source
  chunks and display projection needed for that target are complete.

### Requirement: Review checkpoint anchors preserve exact causal history

The desktop SHALL resume Python-owned causal filtering only from a completed
checkpoint whose recording identity, raw-manifest identity, sampling rate,
recorded source-channel order and V unit, filter settings, Python filter
contract, contiguous-segment origin, and next sample counter exactly match the
requested work. When no compatible anchor exists, it SHALL process every raw
sample from the current contiguous segment origin to the target in sample-
counter order. It SHALL NOT substitute a bounded warm-up interval and describe
the result as causally continuous.

#### Scenario: A compatible earlier anchor exists

- **WHEN** review requests a filtered target after a completed compatible
  anchor in the same contiguous recorded segment
- **THEN** filtering SHALL restore that anchor and process every subsequent raw
  sample through the target in recorded channel order
- **AND** the target output SHALL remain V/float64 until the explicit display
  conversion boundary.

#### Scenario: No compatible anchor exists

- **WHEN** a cold request targets a later position in a contiguous segment
- **THEN** review SHALL begin causal processing at that segment's origin
- **AND** it SHALL process all preceding samples in bounded batches before
  presenting the target as filtered
- **AND** it SHALL NOT claim that a shortened warm-up produces exact
  continuity.

#### Scenario: Recorded history contains a gap

- **WHEN** the processing path reaches a sample-counter discontinuity
- **THEN** review SHALL discard the preceding causal state
- **AND** it SHALL start a new checkpoint chain at the first recorded sample
  after the gap
- **AND** it SHALL not generate or filter fabricated samples for the gap.

### Requirement: Long-record anchor generation is bounded and foreground-aware

The desktop SHALL generate missing checkpoint anchors using bounded raw windows,
bounded concurrency, and coalesced target work. The active user target SHALL
take priority over speculative background generation, while obsolete seek
targets SHALL not create an unbounded queue. The previously completed waveform
SHALL remain visible until the exact target frame is complete.

#### Scenario: A user jumps far into a cold long recording

- **WHEN** no compatible anchor is close enough to satisfy the target directly
- **THEN** review SHALL report that exact filter history is being prepared
- **AND** it SHALL expose progress derived from processed and required sample
  counters rather than wall-clock estimates
- **AND** it SHALL keep the prior complete frame visible
- **AND** it SHALL commit the target position and waveform atomically only when
  the exact target frame is complete.

#### Scenario: The user changes the target during anchor generation

- **WHEN** a newer seek supersedes an unfinished target
- **THEN** review SHALL coalesce foreground work around the latest target
- **AND** reusable completed anchors SHALL remain available
- **AND** obsolete frame results SHALL not replace the latest requested frame.

#### Scenario: Nearby history is prepared in the background

- **WHEN** foreground review is idle and bounded capacity is available
- **THEN** review MAY extend the compatible checkpoint chain in sample-counter
  order
- **AND** foreground navigation, playback, and disposal SHALL preempt or cancel
  that speculative work.

### Requirement: Checkpoint-anchor storage is disposable and validated

The desktop SHALL store only completed checkpoint anchors outside the immutable
recording directory using atomic publication and a versioned compatibility key.
Anchor storage SHALL have explicit size and concurrency bounds. Corrupt,
partial, incompatible, or evicted anchors SHALL be treated as cache misses and
rebuilt from immutable raw samples without changing the raw recording.

#### Scenario: Anchor publication is interrupted

- **WHEN** generation is cancelled or fails before an anchor is fully written
- **THEN** the incomplete entry SHALL not be discoverable as a usable anchor
- **AND** a later request SHALL resume from an earlier completed compatible
  anchor or the contiguous segment origin.

#### Scenario: Filter or recording provenance changes

- **WHEN** any compatibility-key field differs from a stored anchor
- **THEN** review SHALL reject that anchor as a cache hit
- **AND** it SHALL rebuild causal state for the new provenance without deleting
  or rewriting raw data.

#### Scenario: Anchor preparation fails

- **WHEN** raw reading, Python filtering, checkpoint validation, or cache
  publication fails for the active target
- **THEN** review SHALL retain the last complete frame
- **AND** it SHALL expose an explicit failure state that does not claim the
  selected filter was applied to the target.

### Requirement: Mouse wheel navigates recording time
The recording review waveform SHALL move the playback cursor forward by one recording second per downward mouse-wheel detent and backward by one recording second per upward detent. High-resolution wheel deltas SHALL move proportionally. Navigation SHALL reuse the existing relative-seek path, clamp to recording boundaries, preserve playback rate and play/pause state, and update the analysis selection through the existing cursor-selection flow. The visible time scale SHALL remain unchanged.

#### Scenario: Scroll over the waveform
- **WHEN** the user scrolls one downward detent over the recording review waveform
- **THEN** the selected recording time advances by one second, bounded by the recording duration
- **AND** one upward detent moves backward by one second, bounded by zero

### Requirement: Review navigation does not restore obsolete targets

Review SHALL preserve the latest requested playback position and derived viewport
when source preparation or older frame loading reports progress. A newer cached
target SHALL supersede older outstanding foreground work just as an uncached
target does. Only complete, current-identity frames MAY be published.

#### Scenario: Preparation reports progress during a newer drag
- **WHEN** a user changes the thumb target while an older load is pending
- **THEN** progress and completion notifications SHALL NOT restore the older
  requested viewport or position.

#### Scenario: A cached target supersedes an unfinished cold load
- **WHEN** navigation returns to a cached viewport before a cold target finishes
- **THEN** that cached frame SHALL remain displayed
- **AND** the old load SHALL NOT subsequently publish its frame.

### Requirement: Review thumb dragging preserves pointer and clock ownership

The navigator SHALL follow pointer input across the full recording without
programmatically repositioning the operating-system pointer. Waveform work SHALL
remain coalesced and bounded independently of pointer speed. The single playback
clock SHALL pause during a thumb drag and restore its previous running state at
drag completion or capture loss.

#### Scenario: The user drags in either direction while playback is running
- **WHEN** the user starts and then completes a forward or backward thumb drag
- **THEN** the mouse SHALL NOT be moved by the application
- **AND** playback ticks SHALL NOT move the target during the drag
- **AND** playback SHALL resume from the final target after the drag.

### Requirement: Review reuses immutable trace data and actual plot coordinates

Status/progress notifications, annotation changes, playback ticks and unchanged
visible-duration notifications SHALL NOT rebuild identical trace arrays.
The playhead and annotations SHALL use the actual displayed axis coordinates,
including while an uncached target is loading. Numeric display projection SHALL
preserve values, units, temporal extrema and genuine missing-data breaks.

#### Scenario: A target is not covered by the displayed frame
- **WHEN** a new target frame is loading
- **THEN** the chart axis SHALL immediately follow the requested viewport
- **AND** the playhead SHALL remain visible at the requested position
- **AND** the full source-cache range SHALL NOT replace the requested coordinate system.
- **AND** the complete target frame SHALL be published atomically when ready.

#### Scenario: Only status, cursor or annotations change
- **WHEN** the immutable frame and physical vertical scale are unchanged
- **THEN** SciChart trace data SHALL remain unchanged
- **AND** a changed frame or physical scale SHALL still refresh the waveform.
