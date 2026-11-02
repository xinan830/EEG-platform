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

The system SHALL execute current standalone `rbp` on explicitly selected ordered channels in static or dynamic mode and return Delta 1–4, Theta 4–8, Alpha 8–13, Beta 13–30 and Gamma 30–50 Hz relative powers from channel-independent 1–50 Hz PSDs. Each power SHALL use boundary-aware integration; the denominator SHALL be the sum of all five powers. Source PSD evidence SHALL declare reference, continuous 1–50 Hz bandpass, 50 Hz notch Q=30, Welch settings, frequency grid and units. Gamma SHALL be identified as filtered power. Nyquist frequency MUST exceed 50 Hz. Current execution SHALL use `official-rbp-v4`, `rbp-runtime-v5-channel-quality` and Definition 2.0.4 without rewriting historical Runs or adding retired execution fallbacks.

Finite artifact-bearing segments SHALL participate in standalone RBP Welch with channel-specific amplitude, suspected-clipping and low-amplitude warnings, without a minimum clean-segment-ratio gate. Missing/non-finite samples and invalid five-band total power SHALL yield unavailable values for only the affected channel, without imputation or carried-over measurements. Values with warnings SHALL remain visible and SHALL NOT be described as artifact-free. Other algorithms' declared quality policies SHALL remain unchanged.

#### Scenario: Static RBP result
- **WHEN** valid channels and a static range are submitted
- **THEN** each computable channel returns five ratio shares summing approximately to one, band powers, actual range, channel quality and spectral evidence
- **AND** frontend displays backend values without recalculating or renormalizing

#### Scenario: Dynamic RBP result
- **WHEN** a dynamic window contains both healthy and artifact-bearing computable channels
- **THEN** values remain visible with independent channel warnings and the actual time bounds
- **AND** whole-window aggregate quality is partial whenever a channel has warnings or unavailable results

#### Scenario: RBP quality failure
- **WHEN** a channel window has missing/non-finite samples or an invalid denominator
- **THEN** that channel has unavailable shares and a structured reason while unaffected channels remain usable
- **AND** invalid recording sampling rates are rejected without falling back to four bands or fabricated values
