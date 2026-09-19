## MODIFIED Requirements

### Requirement: Acquisition lifecycle has a single local owner

The desktop client SHALL serialize discovery, stream opening, recording,
stopping, and fault cleanup through one acquisition coordinator. A desktop
device session SHALL be the sole owner of published discovery and selected
device facts, while the coordinator remains the sole owner of the native
stream and raw recording lifecycle. Neither owner SHALL depend on the WPF
dispatcher, the Python process, or the backend SQLite database.

#### Scenario: A device stream is opened

- **WHEN** a validated device adapter opens an EEG stream
- **THEN** the coordinator SHALL enter `recording` only after validating its stream metadata and creating raw storage
- **AND** it SHALL retain a session identity, the device-reported channel table, sampling rate, and recording start time.

#### Scenario: An adapter is not installed

- **WHEN** the default unavailable adapter receives a discovery or open request
- **THEN** discovery SHALL return no devices
- **AND** opening SHALL fail with an explicit unavailable-adapter error
- **AND** no synthetic EEG batch, channel, sampling rate, or device identity SHALL be returned.

#### Scenario: A device is discovered for setup

- **WHEN** the configured runtime completes device discovery
- **THEN** the desktop device session SHALL publish only descriptors returned
  by the common adapter
- **AND** no page SHALL infer device capabilities, channel count, or sampling
  rates from a display name.

#### Scenario: A stream faults

- **WHEN** the acquisition coordinator reports a stream fault
- **THEN** raw persistence and fault cleanup SHALL remain coordinator-owned
- **AND** the desktop device session SHALL publish the corresponding faulted
  hardware snapshot without fabricating a reconnect or a device descriptor.
