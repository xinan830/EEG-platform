## MODIFIED Requirements

### Requirement: Desktop shell does not fabricate live acquisition

The desktop shell SHALL expose device and acquisition state explicitly. It
SHALL start with no configured live stream, but SHALL permit a user to opt in
to the installed ANT/eego adapter by supplying an explicit local SDK path and
explicit reference/bipolar ranges. It SHALL not display generated channels,
sample rates, EEG samples, impedance values, or analysis results.

#### Scenario: No acquisition adapter is installed

- **WHEN** the desktop shell starts without applied ANT/eego configuration
- **THEN** it SHALL show device state as not configured
- **AND** it SHALL not display generated channels, sample rates, EEG samples,
  impedance values, or analysis results.

#### Scenario: Configuration has not been applied

- **WHEN** the desktop shell starts or configuration fields have not been
  applied
- **THEN** scanning and recording SHALL not create a device or stream
- **AND** the shell SHALL explain that ANT configuration must be applied.

#### Scenario: Device scan succeeds

- **WHEN** the configured ANT adapter returns one or more devices
- **THEN** the shell SHALL display only the returned device identities and
  supported sampling rates
- **AND** it SHALL require a selected device and rate before recording.

#### Scenario: Recording starts and stops

- **WHEN** the selected device stream opens successfully
- **THEN** the shell SHALL show the actual stream metadata and coordinator
  state
- **AND** stopping or closing the desktop shell SHALL complete or abort local
  raw recording and release the stream.
