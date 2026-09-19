# desktop/wpf-shell Specification

## Purpose
TBD - created by archiving change add-wpf-desktop-shell. Update Purpose after archive.
## Requirements
### Requirement: Desktop shell reports local scientific-engine connectivity

The desktop shell SHALL query the configured local backend health endpoint using a bounded timeout and display backend availability separately from acquisition state.

#### Scenario: Local backend responds successfully

- **WHEN** the backend responds to `GET /api/health` with `status: ok`
- **THEN** the desktop shell SHALL show the scientific engine as available
- **AND** it SHALL not infer that an EEG device is connected

#### Scenario: Local backend is unavailable

- **WHEN** the health request times out, fails to connect, or returns an invalid response
- **THEN** the desktop shell SHALL show the scientific engine as unavailable with a readable diagnostic
- **AND** the desktop process SHALL remain usable

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

### Requirement: Desktop shell preserves scientific ownership boundaries

The desktop shell SHALL use the existing backend API only for its initial connectivity check and SHALL NOT write the backend SQLite database, create scientific artifacts, or recompute EEG algorithms.

#### Scenario: User refreshes connection state

- **WHEN** the user refreshes local connection state
- **THEN** the desktop shell SHALL only request backend health information
- **AND** it SHALL not create an AnalysisRun or modify scientific data

