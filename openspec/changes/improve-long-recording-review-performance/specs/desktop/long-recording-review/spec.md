# Long-Recording Review Performance

## ADDED Requirements

### Requirement: Review performance stages are observable without raw-data leakage

The review pipeline SHALL expose bounded diagnostic evidence for navigation,
raw read, filter/checkpoint work, result lookup or computation, frame publication,
and chart binding. Evidence SHALL identify the recording, session, target,
source path, point count, and elapsed time without logging raw EEG values.

#### Scenario: A user seeks to a cold location

- **WHEN** a review target is outside the current complete frame
- **THEN** diagnostics SHALL distinguish raw read, filter/checkpoint processing,
  result lookup/computation, and frame publication timings
- **AND** the record SHALL include the requested absolute sample/time and source
  path
- **AND** it SHALL not include the complete waveform payload.

### Requirement: Review keeps target and loaded frame identities separate

The review SHALL maintain the newest requested target separately from the last
complete loaded/published frame. An obsolete response SHALL NOT replace a newer
target or relabel older samples with newer time coordinates.

#### Scenario: A user changes target during an outstanding load

- **WHEN** target B supersedes target A before A completes
- **THEN** A SHALL be cancelled or ignored when possible
- **AND** A SHALL NOT publish a frame for B's viewport
- **AND** a complete current-identity frame SHALL be required before replacing
  the published frame.

### Requirement: Visible waveform data remains bounded

Review SHALL bound chart points, frame memory, in-flight requests, and derived
cache usage by explicit budgets independent of total recording duration.

#### Scenario: A multi-hour recording is played continuously

- **WHEN** playback crosses many viewport boundaries
- **THEN** chart series and frame memory SHALL remain within their configured
  bounds
- **AND** completed old frames SHALL be evictable without changing raw data or
  historical results
- **AND** playback ticks inside a cached viewport SHALL not append an unbounded
  history series.

### Requirement: Long recordings support resolution-appropriate waveform display

The review SHALL select a display level from visible absolute time range,
sampling rate, plot width, channel count, and source identity. Each level SHALL
preserve ordered temporal extrema and explicit invalid/gap states.

#### Scenario: A user views a day-scale overview

- **WHEN** the visible range is too wide for sample-level rendering
- **THEN** review SHALL read a compatible overview level instead of all raw
  samples in the recording
- **AND** each bucket SHALL preserve its valid extrema in temporal order
- **AND** recorded gaps SHALL remain visible breaks.

#### Scenario: A user zooms into a short interval

- **WHEN** the visible range becomes small enough for a finer level
- **THEN** review SHALL select a finer compatible level or bounded raw samples
- **AND** the displayed values SHALL retain the declared unit and channel order.

### Requirement: Filtered display levels preserve filter identity

A filtered waveform level SHALL be derived from the compatible filtered source
identity, including filter settings and filter-contract version. Raw waveform
levels SHALL NOT satisfy a filtered display request.

#### Scenario: A user reviews a filtered waveform

- **WHEN** the selected high-pass, low-pass, notch, or filter contract differs
  from a cached level
- **THEN** the level SHALL be treated as a cache miss
- **AND** review SHALL not label raw or differently filtered values as the
  requested filtered waveform.

### Requirement: Completed full-analysis windows are read without duplicate work

When a complete full-analysis result matches the recording, montage, filter,
algorithm version, parameters, and exact scientific window identity, review
SHALL read that result without invoking algorithm computation or filter warmup.

#### Scenario: A full Run is complete before review navigation

- **WHEN** the user seeks to a window covered by the complete compatible Run
- **THEN** the result SHALL be served by indexed read or cache lookup
- **AND** no duplicate algorithm or filter execution SHALL be scheduled
- **AND** the response SHALL retain its original provenance.

#### Scenario: The full result is absent or incompatible

- **WHEN** no compatible complete window exists
- **THEN** review MAY use the existing bounded latest-only on-demand path
- **AND** the unavailable reason/source path SHALL remain observable.

### Requirement: Long-recording derived indexes are disposable and atomic

Display indexes and caches SHALL be stored outside immutable recordings with a
versioned compatibility identity, bounded storage policy, and atomic publication.
Incomplete, corrupt, stale, or incompatible entries SHALL be treated as misses.

#### Scenario: Index generation is interrupted

- **WHEN** index generation is cancelled or fails before completion
- **THEN** the partial entry SHALL not be readable as a complete level
- **AND** a later request SHALL rebuild or use the existing bounded fallback
  without modifying the raw recording.
