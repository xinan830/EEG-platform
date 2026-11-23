# ANT eego adapter delta

## MODIFIED Requirements

### Requirement: The vendor-native stream is serialized process-wide

The adapter SHALL permit no more than one active vendor stream, either EEG or
impedance, in the desktop process and SHALL close the stream before closing its
amplifier/native runtime ownership. Failed native release SHALL NOT make that
ownership available for a new stream until cleanup succeeds or the process
restarts.

#### Scenario: A second stream is requested while one is active

- **WHEN** another ANT/eego stream open is requested while a stream is active
- **THEN** the request SHALL fail with an explicit already-active error
- **AND** the active stream SHALL remain unchanged.

#### Scenario: EEG and impedance opens overlap

- **WHEN** an EEG open overlaps an impedance open for the configured native runtime
- **THEN** at most one request acquires stream ownership
- **AND** the other request fails before opening a native stream

#### Scenario: Native close fails

- **WHEN** a stream or amplifier cannot be closed successfully
- **THEN** the error is surfaced and new stream requests remain blocked
- **AND** ownership is not silently released in a finalization path
