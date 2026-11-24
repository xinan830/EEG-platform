# desktop/acquisition-core Specification

## Purpose
TBD - created by archiving change add-wpf-desktop-shell. Update Purpose after archive.
## Requirements
### Requirement: Acquisition lifecycle has a single local owner

The desktop client SHALL serialize discovery, stream opening, recording, stopping, and fault cleanup through one acquisition coordinator. The coordinator SHALL not depend on the WPF dispatcher, the Python process, or the backend SQLite database.

#### Scenario: A device stream is opened

- **WHEN** a validated device adapter opens an EEG stream
- **THEN** the coordinator SHALL enter `recording` only after validating its stream metadata and creating raw storage
- **AND** it SHALL retain a session identity, the device-reported channel table, sampling rate, and recording start time.

#### Scenario: An adapter is not installed

- **WHEN** the default unavailable adapter receives a discovery or open request
- **THEN** discovery SHALL return no devices
- **AND** opening SHALL fail with an explicit unavailable-adapter error
- **AND** no synthetic EEG batch, channel, sampling rate, or device identity SHALL be returned.

### Requirement: Incoming EEG batches preserve time and unit contracts

The acquisition core SHALL represent incoming data as sample-major float64 values with a unit declared per channel: EEG is V, sample counter is count, and trigger is code. It SHALL retain an explicit device-reported channel list and a sample-counter channel. It SHALL persist PC receive time only as transport diagnostic metadata; sample-counter and sampling-rate semantics remain the scientific time axis.

#### Scenario: Device sample counters skip values

- **WHEN** a received batch begins after the next expected sample counter
- **THEN** the core SHALL record the missing counter interval as a gap
- **AND** it SHALL not create interpolated or zero-valued samples.

#### Scenario: Device sample counters reset or arrive out of order

- **WHEN** a received batch begins before the next expected sample counter
- **THEN** the core SHALL fail the stream with an explicit continuity error
- **AND** it SHALL preserve the prior raw data and audit trail.

### Requirement: Raw persistence precedes display and analysis

The acquisition core SHALL write every accepted batch to local raw chunks before adding it to display history or offering it to analysis. Raw chunks SHALL have a manifest recording their sample-major float64/V format, sampling rate, channel table, sample-counter channel, session identity, and recording start time.

#### Scenario: Display history is full

- **WHEN** the bounded display buffer needs to evict old batches
- **THEN** only display history SHALL be evicted
- **AND** previously accepted raw chunks SHALL remain unchanged.

#### Scenario: Analysis cannot keep up

- **WHEN** the bounded analysis bridge queue is full or its consumer fails
- **THEN** the core SHALL keep recording raw EEG
- **AND** it SHALL not block the device read loop waiting for analysis.

### Requirement: Live display filtering is backend-owned

The desktop SHALL preserve raw incoming batches before dispatching a bounded
copy to the local scientific backend for display filtering. The backend SHALL
return float64 V values and maintain filter state per live session. The desktop
SHALL use returned filtered batches for waveform display and SHALL not
implement an independent high-pass, low-pass, or notch formula. A live session
MAY configure no notch, a 50 Hz notch, or a 60 Hz notch; an unsupported notch
frequency SHALL be rejected. The notch SHALL affect only EEG reference and
bipolar display columns, never persisted raw values, trigger values, or sample
counters.

#### Scenario: A live filter session processes a batch

- **WHEN** the desktop submits an accepted acquisition batch
- **THEN** only EEG reference and bipolar columns SHALL be filtered
- **AND** counter and trigger values SHALL remain unchanged
- **AND** raw recording persistence SHALL remain unfiltered

#### Scenario: The user enables 50 Hz display suppression

- **WHEN** a live session is configured with a 50 Hz notch
- **THEN** the returned EEG display values SHALL be processed by that session's
  stateful 50 Hz notch before its configured display band-pass
- **AND** the input raw batch and persisted raw recording SHALL remain in V
  without that notch applied

### Requirement: Live display filtering decouples transport blocks from vendor batches

The desktop SHALL preserve every accepted vendor batch for raw persistence,
while the optional display-filter branch SHALL aggregate only contiguous
same-session samples into fixed-duration blocks before IPC. The default block
duration SHALL be 50 ms, derived into a target sample count from the actual
stream sampling rate. The desktop live-filter IPC SHALL use little-endian
float64/V binary payloads with explicit sample-count metadata; the JSON batch
endpoint MAY remain available only for compatibility and SHALL NOT be the
desktop hot path.

#### Scenario: A 4 kHz stream produces small vendor batches

- **WHEN** contiguous batches from a 4 kHz stream reach 200 samples
- **THEN** the display-filter branch SHALL submit one 50 ms binary block
- **AND** raw persistence SHALL retain the original vendor batches unchanged
- **AND** UI rendering SHALL not be scheduled once per vendor batch

#### Scenario: A gap reaches the display-filter aggregator

- **WHEN** an explicit raw sample-counter gap precedes a vendor batch
- **THEN** the aggregator SHALL flush any preceding contiguous block
- **AND** it SHALL not merge samples across the gap
- **AND** the next display-filter block SHALL retain the gap boundary

### Requirement: Live display-filter changes take effect at a sample boundary

The desktop SHALL validate high-pass, low-pass, and notch settings before use.
It SHALL reject a low-pass or enabled notch at or above the actual Nyquist
frequency. During recording or pause, the user MAY replace the display filter
configuration without changing device capture or raw persistence. The desktop
SHALL record the first raw sample counter after the user's action as the
effective boundary. Returned display values strictly before that boundary SHALL
remain from the previous display-filter session; values at and after that
boundary SHALL come from the replacement session. The desktop SHALL retain old
display history and SHALL NOT recalculate, clear, replace, or mutate it due to
the change.

The replacement session MAY receive bounded contiguous raw pre-roll solely to
initialize causal state. That pre-roll output SHALL NOT be displayed and SHALL
NOT rewrite display history. The renderer SHALL make the boundary a new trace
segment without creating a time gap.

#### Scenario: User attempts to change a filter during recording

- **WHEN** a recording user selects a valid new high-pass, low-pass, or notch
  value
- **THEN** raw acquisition and persistence SHALL continue unchanged
- **AND** display values before the recorded boundary SHALL remain unchanged
- **AND** the replacement session SHALL start at that boundary without
  displaying its pre-roll output
- **AND** the renderer SHALL not join previous and replacement traces with a
  line

#### Scenario: Warm-up context reaches a gap

- **WHEN** the most recent raw display history is preceded by a sample-counter
  gap
- **THEN** replacement filter pre-roll SHALL begin after that gap
- **AND** the desktop SHALL not interpolate, zero-fill, or include data before
  the gap

#### Scenario: A pending change is superseded

- **WHEN** a user selects another valid filter configuration before a pending
  change reaches its effective boundary
- **THEN** only the newest pending configuration SHALL take effect
- **AND** the renderer SHALL not retain a boundary for the superseded change

### Requirement: Acquisition device drivers are explicitly registered

The desktop SHALL create device adapters only through an explicit registry of
installed drivers. The configured acquisition runtime SHALL depend on the
common driver and adapter contracts and SHALL NOT instantiate or reference a
vendor-specific adapter. A driver configuration SHALL contain a stable driver
identity plus opaque vendor settings that only the selected driver interprets.

#### Scenario: A second vendor driver is installed

- **WHEN** a registered non-ANT driver receives a valid configuration
- **THEN** the configured runtime SHALL create and use that driver's common
  acquisition adapter for discovery and streaming
- **AND** the coordinator, raw writer, display pipeline, and Python analysis
  boundary SHALL remain vendor-neutral

#### Scenario: A selected driver is not installed

- **WHEN** configuration names a driver absent from the registry
- **THEN** configuration SHALL fail with an explicit unavailable-driver error
- **AND** no vendor SDK, device, channel table, sampling rate, or EEG batch
  SHALL be fabricated

### Requirement: Device adapter ownership is released by the common lifecycle

Every common acquisition adapter SHALL support asynchronous disposal. The
configured runtime SHALL dispose its coordinator and active stream before
disposing the adapter.

#### Scenario: Reconfiguration or application shutdown occurs

- **WHEN** the configured runtime replaces or disposes an adapter
- **THEN** it SHALL stop and dispose the coordinator before disposing the
  vendor adapter
- **AND** a vendor implementation SHALL remain responsible for its SDK-native
  stream and handle teardown

### Requirement: Display montage profiles preserve their source-channel contract

The desktop SHALL persist a user-created display montage as a named profile
that includes a snapshot and semantic fingerprint of its source channel
configuration. A user-created montage SHALL contain exactly one fixed derived
row for every named display source channel, preserving the source
configuration's order. A row may use original hardware-reference output,
`A - B`, or `A - Mean(B,C,...)` only. The desktop MAY also expose curated,
read-only system montage presets whose output rows are a documented subset of
the source channels, such as standard bipolar chains or a single-channel
reference that omits its own self-reference row.
The average-reference group SHALL be a profile-level source set shared by all
average-reference rows; it MAY include the row's positive channel. The desktop
SHALL expose both an `平均参考` mode (two or more selected channels) and a
separate `指定双参考` mode (exactly two selected channels). Both retain the
explicit `A - Mean(group)` formula and their distinct user-facing semantics.
The desktop
SHALL NOT alter raw acquisition data, hardware REF/GND wiring metadata, or the
independently selected scientific analysis reference.

#### Scenario: A user creates an identity display montage

- **WHEN** the user selects a channel configuration with enabled named EEG
  channels and creates a montage
- **THEN** the desktop SHALL create identity outputs from those named channels
- **AND** it SHALL retain the selected channel-configuration snapshot and its
  fingerprint with the montage profile

#### Scenario: The ANT default channel configuration exposes system montage presets

- **WHEN** the validated ANT default channel configuration is available
- **THEN** the desktop SHALL expose read-only presets named `ANT默认通道配置 REF参考`,
  `ANT默认通道配置 平均参考`, `ANT默认通道配置 M1/M2参考`,
  `ANT默认通道配置 Cz参考`, `ANT默认通道配置 纵向双极`, and
  `ANT默认通道配置 横向双极` when their required source labels exist
- **AND** each preset SHALL be marked `系统默认`
- **AND** system presets SHALL not expose edit or delete actions
- **AND** a missing source label SHALL cause only the dependent preset row to be
  omitted, never a fabricated channel or inferred label

#### Scenario: A user edits a display montage

- **WHEN** the user opens a display montage for editing
- **THEN** the desktop SHALL show the source-channel rows and their order as
  fixed fields from the saved channel configuration
- **AND** it SHALL permit only the rereference rule and its controlled source
  selection to change

#### Scenario: A montage uses invalid reference inputs

- **WHEN** a user saves a derived channel with a missing, hidden, duplicate, or
  self-referential source label
- **THEN** the desktop SHALL reject the save with a readable validation error
- **AND** it SHALL not substitute a physical position, hardware REF, or GND

#### Scenario: A common-average reference includes its positive source

- **WHEN** an average-reference group includes the current row's positive EEG
  channel
- **THEN** the desktop SHALL retain that source in the saved group
- **AND** it SHALL represent the formula as `A - Mean(group)` without treating
  the inclusion as an invalid self-differential reference

#### Scenario: A user applies a selected average group to all source rows

- **WHEN** the user selects two or more valid source channels in the shared
  average-reference group and applies it to all rows
- **THEN** every fixed source row SHALL use the same saved average group
- **AND** the desktop SHALL NOT require, infer, or hard-code M1/M2 labels

#### Scenario: A display montage is edited or selected

- **WHEN** the user changes a display montage profile
- **THEN** the desktop SHALL leave accepted raw recording samples unchanged
- **AND** it SHALL not infer or overwrite the scientific analysis reference
