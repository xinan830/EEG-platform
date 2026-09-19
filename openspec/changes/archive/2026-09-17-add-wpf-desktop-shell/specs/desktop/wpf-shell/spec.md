## ADDED Requirements

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

The desktop shell SHALL expose device and acquisition state explicitly and SHALL report the initial acquisition adapter as unavailable until a separately accepted hardware integration is installed.

#### Scenario: No acquisition adapter is installed

- **WHEN** the desktop shell starts without an installed device adapter
- **THEN** it SHALL show device state as not configured
- **AND** it SHALL not display generated channels, sample rates, EEG samples, impedance values, or analysis results

### Requirement: Desktop shell preserves scientific ownership boundaries

The desktop shell SHALL use the existing backend API only for its initial connectivity check and SHALL NOT write the backend SQLite database, create scientific artifacts, or recompute EEG algorithms.

#### Scenario: User refreshes connection state

- **WHEN** the user refreshes local connection state
- **THEN** the desktop shell SHALL only request backend health information
- **AND** it SHALL not create an AnalysisRun or modify scientific data
