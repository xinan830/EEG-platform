## ADDED Requirements

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

The acquisition core SHALL represent incoming EEG as sample-major float64 values in V, with an explicit device-reported channel list and a sample-counter channel. It SHALL persist PC receive time only as transport diagnostic metadata; sample-counter and sampling-rate semantics remain the scientific time axis.

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
