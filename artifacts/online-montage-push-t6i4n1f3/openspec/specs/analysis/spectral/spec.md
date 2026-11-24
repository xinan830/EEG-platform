# Spectral analysis baseline

## Purpose

Lock the existing `offline-spectral-v3` PSD, band-power, relative-power, time-range, and quality semantics during platform migration.
## Requirements
### Requirement: Apply the frozen spectral preprocessing contract

`offline-spectral-v3` SHALL use the original recording reference without software rereference, whole-recording zero-phase 1-30 Hz filtering, no notch filter, and `float64` calculations in volts.

#### Scenario: Request a configured spectrum

- **WHEN** a valid recording range and channel list are submitted
- **THEN** those fixed scientific parameters are echoed in the response and viewer filter settings do not alter them

### Requirement: Apply the current versioned spectral preprocessing contract

`offline-spectral-v4` SHALL process algorithm input independently from waveform display filters, use the original recording reference without software rereference, and provide a 1–50 Hz analysis bandpass baseline with zero-phase filtering on float64 values in volts. Algorithms MAY declare a scientifically required bandpass and SHALL record the effective range. V4 SHALL default to a 50 Hz notch with Q=30; an explicit user setting MAY disable it. The effective notch, bandpass, phase, and contract version SHALL be included in result provenance and cache/run identity. `offline-spectral-v3` SHALL remain frozen as a historical contract and SHALL NOT be reinterpreted or reused as v4.

#### Scenario: Run with default analysis preprocessing
- **WHEN** a spectral algorithm runs without overriding preprocessing defaults
- **THEN** execution uses the v4 contract's 1–50 Hz baseline and 50 Hz notch at Q=30
- **AND** result provenance records effective settings and the v4 contract version.

#### Scenario: Disable the analysis notch
- **WHEN** a user explicitly selects notch off for a compatible algorithm
- **THEN** preprocessing skips the notch while retaining the declared bandpass
- **AND** run identity and provenance distinguish it from a notched run.

#### Scenario: Request coverage beyond Nyquist
- **WHEN** the effective analysis high cutoff or requested spectral output reaches or exceeds Nyquist
- **THEN** the backend returns a structured unavailable or validation result
- **AND** it does not silently truncate or relabel the range as 1–50 Hz.

### Requirement: Compute Welch PSD with explicit segmentation

Static and dynamic PSD SHALL use 4-second Hann segments with 2-second overlap/step and constant detrending. Static analysis defaults to a selected 30-second range; dynamic analysis uses the latest 10 seconds and refreshes at a 1-second product step.

#### Scenario: Analyze a clean 30-second range

- **WHEN** a full 30-second window is analyzed
- **THEN** 14 candidate Welch segments are evaluated at starts `0,2,...,26` relative to the selected range

#### Scenario: Analyze a clean 10-second dynamic range

- **WHEN** a complete 10-second dynamic window is analyzed
- **THEN** 4 candidate Welch segments are evaluated and the response reports its actual absolute range

### Requirement: Use non-overlapping band definitions

Band integration SHALL use Delta `[1,4)`, Theta `[4,8)`, Alpha `[8,13)`, and Beta `[13,30]` Hz with boundary-aware trapezoidal integration.

#### Scenario: Sum relative band power

- **WHEN** all four band powers are available for a clean channel
- **THEN** their relative powers sum to approximately one within documented frequency discretization tolerance

### Requirement: Preserve quality and missing-result semantics

PSD SHALL average only clean Welch segments. A result SHALL fail its quality gate when fewer than 75 percent of candidate segments are clean, and unavailable scientific values SHALL be `null` rather than zero.

#### Scenario: Reject a low-quality range

- **WHEN** the clean-segment ratio is below the fixed threshold
- **THEN** the response identifies the quality failure and does not represent missing PSD or band power as measured zero power

### Requirement: Return ordered, unit-bearing output

The spectral API SHALL return requested channels in request order, PSD in `uV^2/Hz`, band power in `uV^2`, relative power as ratio, the algorithm-requested frequency points supported by the effective analysis range, the algorithm version, and requested/actual time range. The shared spectral capability SHALL support 1–50 Hz coverage without requiring every algorithm to emit that entire range.

#### Scenario: Request ordered channels

- **WHEN** the client requests `Oz,Fz,Pz`
- **THEN** all channel-indexed outputs preserve `Oz,Fz,Pz`

### Requirement: Report expanded quality reasons

Spectral window quality SHALL use stable reason codes including `non_finite`, `amplitude_threshold`, `flatline`, `clipping`, and `missing_samples`. Every rejected window SHALL report at least one applicable reason.

#### Scenario: Detect a flat segment

- **WHEN** a segment's per-channel variation is below the declared flatline threshold
- **THEN** the segment is rejected with reason `flatline`

#### Scenario: Detect repeated clipping

- **WHEN** a segment contains a run or proportion of samples at a declared acquisition limit
- **THEN** the segment is rejected with reason `clipping`

#### Scenario: Detect incomplete samples

- **WHEN** an expected complete window contains fewer samples than required by its sampling frequency and duration
- **THEN** the segment is rejected with reason `missing_samples`

### Requirement: Preserve unavailable values

Scientific output rejected by a quality gate SHALL serialize as null/non-value with quality provenance and SHALL NOT be coerced to numerical zero.

#### Scenario: Entire requested range fails quality

- **WHEN** insufficient clean segments remain for PSD averaging
- **THEN** PSD, band power, and relative power are unavailable, quality explains the rejection, and zero is not emitted as a substitute measurement

### Requirement: Separate spectral preprocessing and output frequency ranges
The spectral loading boundary MUST accept independent filter and output
frequency ranges. The preprocessing cache identity MUST include the effective
filter range, notch setting, and contract version, while output clipping MUST
use only the independently requested output range. Provenance SHALL report
both ranges and the effective notch; algorithm window and Welch policy remain
independently declared.

#### Scenario: Dynamic PSD uses configured filtering with a fixed trend axis
- **GIVEN** a dynamic PSD config with high-pass 0.5 Hz, low-pass 40 Hz, and
  notch 50 Hz
- **WHEN** a dynamic window is loaded
- **THEN** preprocessing uses 0.5-40 Hz and 50 Hz notch
- **AND** returned frequency bins are clipped to the configured trend axis
  1-50 Hz
- **AND** evidence records both filter and output ranges
