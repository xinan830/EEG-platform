## ADDED Requirements

### Requirement: Recording duration excludes preview and paused samples
The desktop SHALL calculate effective recording duration from the recording sample origin and sampling rate, independently from the continuous live display timeline. Until a recording sample origin is available, duration SHALL be zero and preview data SHALL NOT create recording pause accounting. Acquisition lifecycle boundaries SHALL determine recorded intervals when available.

#### Scenario: Recording begins after an existing preview
- **WHEN** preview already contains ten seconds of samples and recording is requested
- **AND** no recording batch has been accepted yet
- **THEN** both monitor refresh and waveform rendering SHALL display zero recording duration
- **AND** the live display sample origin SHALL remain unchanged

#### Scenario: Recording is paused before its first sample
- **WHEN** recording is paused before the first recording batch and preview batches continue to arrive
- **THEN** recording duration SHALL remain zero
- **AND** after resuming and receiving one second of recorded samples, duration SHALL be one second
- **AND** these rules SHALL apply with lifecycle boundaries or explicit recording origins and without assuming device counters start at zero

#### Scenario: Continuous preview across repeated recording pauses
- **WHEN** recording has accepted samples and is paused and resumed repeatedly while preview continues
- **THEN** only recorded intervals SHALL increase effective duration
- **AND** resuming before its first resumed batch SHALL preserve the completed duration
- **AND** the raw sample timeline used to draw live waveforms SHALL remain continuous
