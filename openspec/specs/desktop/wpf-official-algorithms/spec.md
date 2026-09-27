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

### Requirement: Configure declared dynamic modes

The WPF algorithm workflow SHALL expose dynamic mode only for catalog entries
that declare `dynamic`, and SHALL populate window and step choices from the
selected algorithm's `dynamic_policy`.

#### Scenario: Dynamic policy is declared

- **WHEN** a runnable algorithm advertises dynamic mode
- **THEN** the client shows dynamic mode and the backend-provided window and
  step defaults/options.

#### Scenario: Dynamic mode is unavailable

- **WHEN** an algorithm does not advertise dynamic mode
- **THEN** the client keeps the static workflow and does not submit a dynamic
  configuration.

### Requirement: Submit dynamic runs without client science

The WPF client SHALL submit `mode`, `dynamic_window_s`, and `refresh_step_s`
to the existing official algorithm Run endpoint and SHALL NOT calculate EEG
metrics locally.

#### Scenario: Submit a dynamic run

- **WHEN** the user selects dynamic mode and submits a valid range
- **THEN** the client sends the selected channel, range, mode, window, and step
  to the existing official algorithm endpoint.

### Requirement: Render dynamic result provenance

The client SHALL display backend-returned time points or structured window
states, units, and quality summaries. `Partial`, `Complete`, `Rejected`, and
`Unavailable` SHALL remain distinguishable, and null values SHALL NOT be shown
as zero.

#### Scenario: Display dynamic states

- **WHEN** a completed dynamic Run contains multiple window states
- **THEN** the client displays the backend window count, state counts, and unit
  without changing the returned values.

### Requirement: Preserve static workflows

Adding dynamic controls SHALL NOT change static request payloads or existing
static result rendering.

#### Scenario: Run an existing static algorithm

- **WHEN** the user keeps static mode selected
- **THEN** the client sends the existing static payload and renders the existing
  scalar or structured result path.

### Requirement: Preserve dynamic RBP bands

Dynamic RBP SHALL return Delta, Theta, Alpha, and Beta relative-power values
for every planned window. The client SHALL display those backend values without
reducing them to one scalar or recomputing them.

#### Scenario: Dynamic RBP window

- **WHEN** a dynamic RBP Run completes for a valid window
- **THEN** the result contains all four band values and the window state.

### Requirement: WPF submits static Band Ratio with two explicit frequency bands

WPF SHALL allow Band Ratio in static mode and SHALL submit one registered
channel, exact time range, numerator and denominator band edges, algorithm ID,
and scientific version. It SHALL reject invalid ordering and Nyquist bounds
before submission.

#### Scenario: Valid Band Ratio request

- **WHEN** the operator selects Band Ratio, a channel, a valid time range, and
  two valid bands below Nyquist
- **THEN** WPF submits those exact inputs to one official Run

#### Scenario: Invalid Band Ratio bands

- **WHEN** either band is missing, nonfinite, negative, reversed, equal, or has
  an upper edge at or above Nyquist
- **THEN** WPF refuses submission with a specific validation message

### Requirement: Band Ratio displays backend-owned scalar output

WPF SHALL display the backend-returned ratio, unit, quality, channel,
requested/actual range, Run identity, and provenance. It SHALL not calculate
the ratio in the client or request a matrix preview.

#### Scenario: Completed Band Ratio result

- **WHEN** the official Run completes with a scalar ratio output
- **THEN** WPF displays the backend value and provenance without client-side
  recomputation

### Requirement: WPF submits static FAA with two distinct source channels

WPF SHALL allow FAA only in static mode and SHALL submit the registered F3
source channel, registered F4 source channel, exact time range, algorithm ID,
and scientific version. It SHALL reject missing, unknown, or identical source
channels before submission.

#### Scenario: Valid FAA request

- **WHEN** the operator selects FAA, two different registered channels, and a
  valid static time range
- **THEN** WPF submits both source channels to one official FAA Run

#### Scenario: Invalid FAA channel selection

- **WHEN** either channel is missing/unknown or both selectors contain the same
  channel
- **THEN** WPF refuses submission with a specific validation message

### Requirement: FAA displays backend-owned scalar output

WPF SHALL display the backend-returned FAA value, unit, quality, source
channels, requested/actual range, Run identity, and provenance. It SHALL not
recompute Alpha power or FAA in the client.

#### Scenario: Completed FAA result

- **WHEN** the official FAA Run completes with a scalar result
- **THEN** WPF displays the backend value and provenance without client-side
  recomputation

### Requirement: WPF submits static IAPF using a registered channel

WPF SHALL allow IAPF in static mode and SHALL submit one registered channel,
exact time range, algorithm ID, and scientific version. It SHALL reject an
unregistered or missing channel before submission.

#### Scenario: Valid IAPF request

- **WHEN** the operator selects IAPF, a registered channel, and a valid static
  time range
- **THEN** WPF submits those exact inputs to one official IAPF Run

### Requirement: IAPF displays backend-owned scalar output

WPF SHALL display the backend-returned IAPF value in Hz, quality, channel,
requested/actual range, Run identity, and provenance. It SHALL not calculate or
interpolate IAPF in the client.

#### Scenario: Completed IAPF result

- **WHEN** the official IAPF Run completes with a scalar result or structured
  unavailable reason
- **THEN** WPF displays the backend value or unavailable state without
  converting null to zero

### Requirement: WPF submits static Peak Frequency with an explicit frequency band

WPF SHALL allow Peak Frequency only in static mode and SHALL submit one
registered backend channel, exact time range, `low_hz`, `high_hz`, algorithm ID,
and scientific version. It SHALL reject invalid band values before submission.

#### Scenario: Valid Peak Frequency request

- **WHEN** the operator selects Peak Frequency, a channel, a valid time range,
  and `low_hz < high_hz < Nyquist`
- **THEN** WPF submits those exact inputs to one official Run

#### Scenario: Invalid frequency band

- **WHEN** either edge is missing/nonfinite/negative, the edges are reversed or
  equal, or the high edge is at or above Nyquist
- **THEN** WPF refuses submission with a specific validation message

### Requirement: Peak Frequency displays backend-owned scalar output

WPF SHALL display the backend-returned peak frequency in Hz, quality, channel,
requested/actual range, Run identity, and provenance. It SHALL not calculate,
interpolate, or request a matrix preview for this scalar result.

#### Scenario: Completed Peak Frequency result

- **WHEN** the official Run completes with a scalar `peak_frequency_hz` output
- **THEN** WPF displays the backend value and its Hz unit together with the
  returned quality, channel, requested/actual range, Run identity, and
  provenance, without requesting a structured matrix preview

### Requirement: WPF submits static RBP using backend-declared inputs

WPF SHALL allow a runnable catalog RBP item in static mode only after a completed
recording is registered. It SHALL submit the selected backend channel, exact
finite in-record start/end seconds, algorithm ID, scientific version, and
`mode=static`. It SHALL not submit dynamic RBP through this form.

#### Scenario: Valid RBP Run

- **WHEN** the operator selects RBP, a registered channel, and a valid range
- **THEN** WPF creates one official Run with those exact inputs
- **AND** WPF retains the Run identity and backend provenance

### Requirement: RBP is displayed as backend-owned band shares

WPF SHALL display backend-returned Delta, Theta, Alpha, and Beta ratio values and
the `ratio` unit. Null values, failed quality, and unavailable results SHALL
remain explicitly unavailable. WPF SHALL not request a matrix preview or
recompute band power.

#### Scenario: Completed RBP result

- **WHEN** the backend returns a completed RBP metric with four band values
- **THEN** WPF displays all four values and the quality/provenance text
- **AND** no PSD curve or STFT bitmap is shown as the RBP result

### Requirement: WPF submits official static STFT with explicit raw inputs

WPF SHALL use a completed, backend-registered recording, one backend-declared raw EEG channel, an explicit finite in-record range of at least four seconds, and the catalog scientific version. It SHALL submit `mode=static` without changing the backend algorithm or raw recording. The dynamic mode SHALL remain unavailable in this form.

#### Scenario: Valid static STFT request

- **WHEN** the operator chooses STFT, a registered channel, and a valid requested start/end range
- **THEN** WPF sends those exact seconds and channel in an official-algorithm Run
- **AND** displays the backend Run identity, requested/actual range, quality, and result status

#### Scenario: Invalid range or channel

- **WHEN** the range is shorter than four seconds, nonfinite, outside the recording, or the channel is absent from the registration response
- **THEN** WPF refuses submission and shows a specific error

### Requirement: STFT preview preserves backend axes, units, quality, and missing cells

WPF SHALL render only a bounded backend-produced static `time x frequency` matrix. Time centers SHALL be labeled in seconds, frequency in Hz, and power in `dB re 1 uV^2/Hz`. WPF SHALL NOT derive spectral power or replace null/nonfinite cells with zero. Invalid shape/unit or a preview over the cell limit SHALL be reported as unavailable without claiming that the completed Run failed.

#### Scenario: A partial preview contains unavailable cells

- **WHEN** a completed Run returns a valid two-dimensional `power_db` matrix containing null cells
- **THEN** WPF leaves those cells visually distinct from numeric power and keeps the backend quality/provenance readable

#### Scenario: Preview exceeds the transport ceiling

- **WHEN** the backend rejects a requested 100,000-cell preview
- **THEN** WPF explains that the saved result is too large for this view and suggests a shorter analysis range

### Requirement: WPF submits static Theta/Beta using a registered channel

WPF SHALL allow Theta/Beta in static mode and SHALL submit one registered
channel, exact time range, algorithm ID, and scientific version.

#### Scenario: Valid Theta/Beta request

- **WHEN** the operator selects Theta/Beta, a registered channel, and a valid
  static time range
- **THEN** WPF submits those exact inputs to one official Run

### Requirement: Theta/Beta displays backend-owned scalar output

WPF SHALL display the backend-returned ratio, quality, channel,
requested/actual range, Run identity, and provenance. It SHALL preserve a
backend unavailable result and SHALL not calculate IAPF or the ratio in the
client.

#### Scenario: Completed or unavailable Theta/Beta result

- **WHEN** the official Run completes with a scalar ratio or structured quality
  failure
- **THEN** WPF displays the backend value or unavailable state without
  converting null to zero

### Requirement: WPF provides standard frequency-band presets without hiding exact values

WPF SHALL provide standard band presets for Peak Frequency and Band Ratio,
populate the existing numeric inputs when selected, and submit the resulting
numeric values. Editing a populated value SHALL mark the selection as Custom.

#### Scenario: Preset selection and manual override

- **WHEN** the operator selects Alpha for Peak Frequency or Theta/Beta for Band
  Ratio
- **THEN** WPF fills the corresponding numeric fields, and after any manual
  edit displays Custom while preserving the edited values for validation and
  submission

### Requirement: Fixed official definitions remain distinct from generic presets

WPF SHALL NOT expose frequency preset editing as a replacement for the fixed
IAPF or official Theta/Beta definitions.

#### Scenario: IAPF remains fixed

- **WHEN** the operator selects IAPF
- **THEN** no generic band preset is shown and the backend IAPF contract remains
  unchanged
