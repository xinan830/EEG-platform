## MODIFIED Requirements

### Requirement: Desktop shell does not fabricate live acquisition

The desktop shell SHALL expose device and acquisition state explicitly. It
SHALL start with no configured live stream and provide an amplifier-settings
surface where a user can choose an explicit local SDK DLL and test the
connection. It SHALL only display devices, rates, and ranges returned by the
SDK, and SHALL require explicit device/rate/reference-range/bipolar-range
selection before recording. It SHALL not display generated channels, sample
rates, EEG samples, impedance values, or analysis results.

#### Scenario: No acquisition adapter is installed

- **WHEN** the desktop shell starts without applied ANT/eego configuration
- **THEN** it SHALL show device state as not configured
- **AND** it SHALL not display generated channels, sample rates, EEG samples,
  impedance values, or analysis results.

#### Scenario: Configuration has not been applied

- **WHEN** the desktop shell starts or no SDK DLL has been selected
- **THEN** testing a connection and recording SHALL not create a device or stream
- **AND** the shell SHALL explain that amplifier configuration is required.

#### Scenario: Device scan succeeds

- **WHEN** the configured ANT adapter returns one or more devices
- **THEN** the shell SHALL display only the returned device identities,
  sampling rates, and reference/bipolar range choices
- **AND** it SHALL require a selected device, rate, and both ranges before recording.

#### Scenario: Recording starts and stops

- **WHEN** the selected device stream opens successfully
- **THEN** the shell SHALL show the actual stream metadata and coordinator
  state
- **AND** stopping or closing the desktop shell SHALL complete or abort local
  raw recording and release the stream.
