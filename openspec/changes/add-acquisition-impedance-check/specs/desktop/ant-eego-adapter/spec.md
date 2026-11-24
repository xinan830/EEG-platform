# ANT eego adapter delta

## MODIFIED Requirements

### Requirement: The vendor-native stream is serialized process-wide

The adapter SHALL permit no more than one active vendor stream, either EEG or
impedance, in the desktop process and SHALL close the stream before closing its
amplifier/native runtime ownership. Failed native release SHALL NOT make that
ownership available for a new stream until cleanup succeeds or the process
restarts.

The native SDK SHALL remain initialized for the lifetime of the desktop
process, across zero-lease gaps between discovery, EEG and impedance operations.
It SHALL receive one SDK exit only during orderly application shutdown after
all leases have been released. The configured SDK library path cannot change
after the process initializes the SDK.

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
- **AND** a later cleanup retry invokes the failed native close again and releases ownership only after success

#### Scenario: Switch modes without restarting the SDK
- **WHEN** the last lease for a discovery, EEG or impedance operation is released
- **THEN** the SDK remains initialized so a subsequent mode can open through the same native runtime
- **AND** SDK exit occurs only after application shutdown confirms no active leases
