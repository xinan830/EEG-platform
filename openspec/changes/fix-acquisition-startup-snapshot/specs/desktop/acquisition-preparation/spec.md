## ADDED Requirements

### Requirement: Preview startup preserves one validated parameter snapshot
The desktop SHALL use one validated snapshot for preview startup, settings persistence and success text. Filter editing SHALL be unavailable throughout preparation and stream opening. Older pending filter updates SHALL finish or be discarded before startup applies the snapshot. Changed non-filter preparation inputs SHALL prevent opening the stream. Raw sample times, units, channel order and quality semantics SHALL remain unchanged.

#### Scenario: Filter change attempted while settings persistence is waiting
- **WHEN** preview startup validates 500 Hz sampling and 70 Hz low-pass and settings persistence is pending
- **AND** a filter edit attempts to change low-pass to 300 Hz
- **THEN** the filter value SHALL remain 70 Hz
- **AND** the stream filter configuration, saved snapshot and success text SHALL agree

#### Scenario: Delayed display update predates startup
- **WHEN** a display filter update is pending when preview startup begins
- **THEN** it SHALL not overwrite the validated startup filter after startup applies it

#### Scenario: Non-filter preparation inputs change during startup
- **WHEN** other preparation inputs change while device configuration or settings persistence is awaiting completion
- **THEN** startup SHALL fail with a readable preparation error before opening the stream
- **AND** filter editing SHALL become available again

#### Scenario: Startup fails
- **WHEN** validation or settings persistence fails
- **THEN** the preparation lock SHALL be released
- **AND** a corrected configuration SHALL be usable on the next attempt
