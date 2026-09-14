# Spectral analysis baseline

## Purpose

Lock the existing `offline-spectral-v3` PSD, band-power, relative-power, time-range, and quality semantics during platform migration.

## Requirements

### Requirement: Apply the frozen spectral preprocessing contract

`offline-spectral-v3` SHALL use the original recording reference without software rereference, whole-recording zero-phase 1-30 Hz filtering, no notch filter, and `float64` calculations in volts.

#### Scenario: Request a configured spectrum

- **WHEN** a valid recording range and channel list are submitted
- **THEN** those fixed scientific parameters are echoed in the response and viewer filter settings do not alter them

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

The spectral API SHALL return requested channels in request order, PSD in `uV^2/Hz`, band power in `uV^2`, relative power as ratio, frequency points from 1 through 30 Hz, the algorithm version, and requested/actual time range.

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
