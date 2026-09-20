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

