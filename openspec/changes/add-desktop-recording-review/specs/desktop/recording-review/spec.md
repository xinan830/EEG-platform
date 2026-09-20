## ADDED Requirements

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

The time navigator MAY update a preview target continuously, but the waveform
SHALL retain its last complete frame until a complete matching frame is ready.
SciChart SHALL NOT bind a partially constructed source chunk or partial montage
frame.

#### Scenario: A user drags across an uncached chunk boundary

- **WHEN** the navigator target enters a chunk that has not finished building
- **THEN** the previous complete waveform SHALL remain visible
- **AND** the target waveform SHALL replace it atomically only after all source
  chunks and display projection needed for that target are complete.
