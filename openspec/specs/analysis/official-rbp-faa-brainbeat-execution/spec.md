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

### Requirement: Use the declared FAA dynamic window default

Dynamic Official FAA requests that omit `dynamic_window_s` SHALL use the FAA
manifest's 20-second default. Requests that explicitly provide a positive
window SHALL preserve that value. This default SHALL not change other
algorithms' defaults.

#### Scenario: Dynamic FAA request omits its window

- **WHEN** a dynamic Official FAA request omits `dynamic_window_s`
- **THEN** its runtime configuration uses a 20-second analysis window and the
  declared refresh-step default
- **AND** the resulting window can contain the minimum 10 paired 2-second
  epochs required by the existing quality gate

#### Scenario: Dynamic FAA request provides a window

- **WHEN** a dynamic Official FAA request explicitly provides a window
- **THEN** the runtime uses the requested window without replacing it with the
  default

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

### Requirement: Publish RBP absolute band power as structured output
The current official RBP result SHALL publish each channel's absolute Delta, Theta, Alpha, Beta, and Gamma band power as structured arrays alongside the existing relative-power arrays. Each output array SHALL have the same channel order and window dimensions as its corresponding relative-power array and SHALL declare `uV^2`. Static, dynamic-series, and on-demand RBP results SHALL use the same output names and units. Absolute power SHALL be the numerator band power already computed from the current RBP PSD; this addition SHALL NOT change the five-band denominator, relative-power values, preprocessing, quality policy, or requested and actual time ranges. Unavailable channel values SHALL remain null/unavailable with the existing per-channel failure reason and SHALL NOT be replaced with zero.

#### Scenario: Static and on-demand RBP output
- **WHEN** a static or on-demand RBP window contains computable channels
- **THEN** each channel returns five absolute-power values in `uV^2` aligned with the existing five ratio arrays and channel order
- **AND THEN** a channel that is unavailable for relative RBP is also unavailable for the corresponding absolute-power values

#### Scenario: Dynamic RBP output
- **WHEN** a dynamic RBP run contains multiple windows
- **THEN** each absolute-power array has the same window-by-channel shape and time axis as its corresponding relative-power array
- **AND THEN** each window retains the existing quality state, failure details, and absolute file-time bounds

#### Scenario: Current result identity
- **WHEN** the expanded structured output contract is resolved for a new RBP result
- **THEN** its Definition and implementation identities distinguish it from results that only declare relative-power arrays
- **AND THEN** its scientific version remains `official-rbp-v4` because the formula and preprocessing are unchanged

### Requirement: Record request-correlated RBP window stage durations
For an on-demand RBP window, backend diagnostics SHALL report the request ID, window ID, recording ID, half-open sample range, spectrum-load duration, and band-integration duration. Existing source-preparation timing SHALL remain distinguishable from those algorithm stages. Diagnostics SHALL NOT include EEG samples or numeric output arrays and SHALL NOT affect the result, cache identity, or scientific version.

#### Scenario: Diagnose a slow RBP window
- **WHEN** an on-demand RBP window takes longer than expected
- **THEN** logs for the same request and window distinguish source preparation, spectrum loading, band integration, and result publication
- **AND THEN** the returned arrays, units, channel order, and window bounds are unchanged
