# desktop/wpf-official-algorithms Specification

## Purpose
TBD - created by archiving change integrate-official-algorithms-into-wpf-client. Update Purpose after archive.
## Requirements
### Requirement: Register a completed WPF recording before analysis

The desktop SHALL register a completed local recording through the backend
registration contract before creating an official Analysis Run. Registration
SHALL carry the immutable manifest identity, sampling rate, actual channel
order and units, recording start UTC, sample-counter segments, and explicit
gaps.

#### Scenario: Valid completed recording is registered

- **WHEN** WPF submits a complete manifest whose referenced chunks exist and
  decode as sample-major float64 V data
- **THEN** the backend returns a stable `recording_id`
- **AND** repeated registration of the same source identity returns the same
  recording identity
- **AND** no scientific Run or artifact is created by registration alone

#### Scenario: Incomplete recording is rejected

- **WHEN** a manifest is missing, incomplete, corrupt, or contains an
  ambiguous channel/unit declaration
- **THEN** registration fails with a structured error code
- **AND** no Run is created
- **AND** raw files remain unchanged

### Requirement: WPF consumes the authoritative official catalog

The WPF algorithm workspace SHALL load algorithm identity, scientific version,
availability, supported modes, output schema, dynamic policy, and parameter
schema from `GET /api/algorithms`. It SHALL not duplicate scientific parameter
rules or infer channel roles from display names or channel positions.

#### Scenario: Catalog item is unavailable

- **WHEN** the backend marks an official item unavailable or non-runnable
- **THEN** WPF shows the reason and prevents Run submission

### Requirement: WPF submits a traceable offline Run

The WPF workspace SHALL submit an official Run only for a successfully
registered Recording and SHALL include the selected algorithm ID, exact
scientific version, explicit source channels, requested range, mode, and
validated parameter values.

#### Scenario: Static Run is submitted

- **WHEN** a user selects a runnable official algorithm and a valid range
- **THEN** WPF creates one Run and retains its `run_id`
- **AND** WPF does not calculate any scientific value locally

### Requirement: WPF tracks Run and result provenance

WPF SHALL poll Run status until a terminal state and SHALL display terminal
errors using structured codes. A completed result SHALL show algorithm identity,
scientific version, requested/actual ranges, channel order, units, quality
state, and backend-returned values or matrices.

#### Scenario: Quality-gated result is returned

- **WHEN** the backend returns `gate_failed`, `Rejected`, or `Unavailable`
- **THEN** WPF displays the state and reason
- **AND** it does not display a fabricated numeric zero

### Requirement: Structured result preview remains backend-owned

WPF SHALL obtain PSD/STFT structured values and axes through the bounded
structured-preview endpoint. Null cells SHALL remain unavailable; WPF SHALL
not perform FFT, dB conversion, interpolation, integration, or quality
classification.

#### Scenario: WPF renders a structured preview

- **WHEN** a completed PSD or STFT Run has a valid structured preview
- **THEN** WPF renders the returned axes, units, values, and window states
- **AND** a returned null cell is shown as unavailable rather than zero
- **AND** no scientific transformation is performed in the desktop process

### Requirement: Real-time algorithms remain out of scope

This specification SHALL NOT define a streaming algorithm Runtime or imply
that an offline Analysis Run is a real-time result. A future streaming change
MUST define its own back-pressure, discontinuity, latency, and sequence
contracts.

#### Scenario: Live acquisition is not submitted as an offline Run

- **WHEN** a recording is still acquiring, paused, or has not passed completed
  manifest validation
- **THEN** WPF does not submit it as an official offline Analysis Run
- **AND** the acquisition and raw-writing state machines continue independently

### Requirement: The static PSD form uses explicit project recording and analysis inputs

WPF SHALL select a completed recording from the project catalog and register it
before enabling a static PSD Run. The operator SHALL explicitly choose one
backend-declared EEG channel and requested start/end range. WPF SHALL reject
empty, non-finite, reversed, or out-of-recording ranges before submission.

#### Scenario: The operator submits static PSD

- **WHEN** a completed project recording is registered and the operator chooses PSD, an available channel, and a valid requested range
- **THEN** WPF submits those exact inputs and the selected scientific version to the backend
- **AND** no other official algorithm is submitted using the PSD-only form

### Requirement: The PSD result is a rendering of backend output

The WPF PSD result view SHALL display the backend-returned frequency axis,
power-density unit, and available values. Null or non-finite preview cells
SHALL remain unavailable and SHALL NOT be replaced by zero.

#### Scenario: A completed PSD Run has a structured preview

- **WHEN** the backend returns a bounded static frequency series
- **THEN** WPF draws only the returned points with explicit axis labels and units
- **AND** requested and actual ranges and Run identity remain visible
