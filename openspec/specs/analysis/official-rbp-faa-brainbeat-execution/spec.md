# analysis/official-rbp-faa-brainbeat-execution Specification

## Purpose
TBD - created by archiving change enable-rbp-faa-brainbeat. Update Purpose after archive.
## Requirements
### Requirement: Execute FAA with explicit paired channels

The system SHALL execute official `faa` only for explicit distinct raw
`f3_channel` and `f4_channel` selections. It SHALL preserve the existing
paired two-second epoch quality gate and return `ln(P_F4) - ln(P_F3)`.

#### Scenario: Static FAA result

- **WHEN** both selected source channels exist and meet the paired FAA quality
  contract
- **THEN** the Run returns FAA, F3/F4 alpha powers, epoch counts, clean ratio,
  actual range and source-channel identities
- **AND THEN** it analyses exactly the requested absolute range without
  applying the legacy realtime pipeline's initial-recording discard

#### Scenario: Missing or unsuitable FAA source channels

- **WHEN** either selected channel is missing, both selections are identical,
  or the paired quality gate fails
- **THEN** the Run returns a structured failure or `null` FAA value with its
  paired-quality reason
- **AND THEN** it does not infer a replacement channel or produce zero

### Requirement: Preserve the stateful BrainBeat boundary

The installed offline Brainbeat module SHALL remain available for static and
dynamic AnalysisRuns as defined in `analysis/official-brainbeat`. Offline
windows SHALL estimate their own IAPF independently and SHALL NOT carry
IAPF-lock or EMA state between windows. Offline availability SHALL NOT imply
that stateful realtime Brainbeat is enabled or scientifically equivalent.
A stateful realtime path SHALL require its own session, IAPF-lock, EMA,
quality, provenance and validation contract before enablement.

#### Scenario: Read BrainBeat in the catalog

- **WHEN** a client loads the official algorithm catalog with the validated offline Brainbeat module installed
- **THEN** Brainbeat is available and runnable for its declared static and dynamic offline modes
- **AND** its scientific version and implementation identity come from its module manifest
- **AND** this entry does not declare a stateful realtime mode

#### Scenario: Offline windows do not inherit realtime state

- **WHEN** two offline Brainbeat windows execute in a different request order
- **THEN** each uses its own window IAPF and source spectra
- **AND** neither consumes prior-window EMA or IAPF-lock state

### Requirement: Execute official RBP as one five-band result

The system SHALL execute the current official `rbp` on one explicitly selected raw channel in static or dynamic mode and return Delta 1–4, Theta 4–8, Alpha 8–13, Beta 13–30 and Gamma 30–50 Hz relative powers from the same quality-gated 1–50 Hz PSD. Each power SHALL use boundary-aware integration. The denominator SHALL be the sum of all five powers. The source PSD SHALL declare its reference, 1–50 Hz bandpass, 50 Hz notch (Q=30), Welch settings, frequency grid and units. Because this notch attenuates frequencies near Gamma's 50 Hz boundary, the result SHALL be identified as filtered Gamma power and its exact preprocessing SHALL remain in provenance. Nyquist frequency MUST exceed 50 Hz. The current five-band Run SHALL use scientific version `official-rbp-v2`, implementation identity `rbp-runtime-v2-five-band` and published Definition version `2.0.1`, preserving immutable four-band historical Runs and Definition `1.0.0`.

#### Scenario: Static RBP result

- **WHEN** a clean valid recording, channel and static range are submitted for current `rbp`
- **THEN** the Run returns five finite band shares in ratio units summing to approximately one, their band powers, source channel, actual range, quality and spectral evidence
- **AND THEN** the frontend renders the backend values without recalculating or renormalizing them

#### Scenario: Dynamic RBP result

- **WHEN** a dynamic RBP Run produces a clean window
- **THEN** that window returns five backend-computed shares and its actual time bounds
- **AND THEN** rejected or unavailable windows contain five null values, a structured reason and no carried-over measurement

#### Scenario: RBP quality failure

- **WHEN** a window fails quality, any band denominator is invalid, or the recording Nyquist frequency cannot cover 50 Hz
- **THEN** the system reports a structured failure without representing unavailable shares as zero or silently reverting to four bands
