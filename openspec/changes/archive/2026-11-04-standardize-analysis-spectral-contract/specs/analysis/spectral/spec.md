## ADDED Requirements

### Requirement: Apply the current versioned spectral preprocessing contract

The new `offline-spectral-v4` shared spectral execution contract SHALL process algorithm input independently from waveform display filters, use the original recording reference without software rereference, and provide a 1–50 Hz analysis bandpass baseline with zero-phase filtering on float64 values in volts. Algorithms MAY declare a narrower or wider scientifically required bandpass and SHALL record the effective range. V4 SHALL default to a 50 Hz notch with Q=30; an explicit user setting MAY disable it. The effective notch, bandpass, phase, and contract version SHALL be included in result provenance and cache/run identity. `offline-spectral-v3` SHALL remain frozen as a historical contract and SHALL NOT be reinterpreted or reused as v4.

The existing Welch segmentation, band definitions, quality semantics, ordered unit-bearing output, unavailable-value, and separate filter/output-range requirements remain unchanged.

#### Scenario: Change the review waveform filter
- **WHEN** a user changes a review-only high-pass, low-pass, or notch setting
- **THEN** an algorithm run's preprocessing and result SHALL remain unchanged
- **AND** the review setting SHALL NOT enter algorithm cache identity or provenance.

#### Scenario: Run with default analysis preprocessing
- **WHEN** a user submits a spectral algorithm without overriding its preprocessing defaults
- **THEN** the execution uses the v4 contract's 1–50 Hz baseline and 50 Hz notch at Q=30
- **AND** the result records the effective settings and v4 contract version.

#### Scenario: Disable the analysis notch
- **WHEN** a user explicitly selects notch off for a compatible algorithm
- **THEN** the backend skips the notch while retaining the declared analysis bandpass
- **AND** the run identity and provenance distinguish it from a run with the notch enabled.

#### Scenario: Requested coverage exceeds the sampling limit
- **WHEN** the effective analysis high cutoff or requested spectral output reaches or exceeds Nyquist
- **THEN** the backend returns a structured unavailable/validation result
- **AND** it does not silently truncate or relabel the range as 1–50 Hz.

### Requirement: Compute Welch PSD with explicit segmentation

Static and dynamic PSD SHALL use 4-second Hann segments with 2-second overlap/step and constant detrending. Static analysis defaults to a selected 30-second range; dynamic analysis uses the latest 10 seconds and refreshes at a 1-second product step. These Welch and algorithm-window settings SHALL remain algorithm-owned and SHALL NOT be inferred from the shared 1–50 Hz preprocessing baseline.

#### Scenario: Analyze a clean 30-second range
- **WHEN** a full 30-second window is analyzed
- **THEN** 14 candidate Welch segments are evaluated at starts `0,2,...,26` relative to the selected range

#### Scenario: Analyze a clean 10-second dynamic range
- **WHEN** a complete 10-second dynamic window is analyzed
- **THEN** 4 candidate Welch segments are evaluated and the response reports its actual absolute range

### Requirement: Use non-overlapping band definitions

Band integration SHALL use Delta `[1,4)`, Theta `[4,8)`, Alpha `[8,13)`, and Beta `[13,30]` Hz with boundary-aware trapezoidal integration. Other algorithms MAY declare different bands, including gamma through 50 Hz, but SHALL state their edges and denominator when emitting relative power.

#### Scenario: Sum relative band power
- **WHEN** all four band powers are available for a clean channel
- **THEN** their relative powers sum to approximately one within documented frequency discretization tolerance

#### Scenario: Include power-line frequency in an algorithm band
- **WHEN** an algorithm computes power in a band that includes 50 Hz while the default 50 Hz notch is enabled
- **THEN** the backend integrates the actual notched spectrum
- **AND** provenance reports the notch so the reduced power is interpretable.

### Requirement: Preserve quality and missing-result semantics

PSD SHALL average only clean Welch segments. A result SHALL fail its quality gate when fewer than 75 percent of candidate segments are clean, and unavailable scientific values SHALL be `null` rather than zero.

#### Scenario: Reject a low-quality range
- **WHEN** the clean-segment ratio is below the fixed threshold
- **THEN** the response identifies the quality failure and does not represent missing PSD or band power as measured zero power

### Requirement: Return ordered, unit-bearing output

The spectral API SHALL return requested channels in request order, PSD in `uV^2/Hz`, band power in `uV^2`, relative power as ratio, the algorithm-requested frequency points supported by the effective analysis range, the algorithm version, and requested/actual time range. The shared spectral capability SHALL support a 1–50 Hz coverage baseline without forcing every algorithm to emit that entire range.

#### Scenario: Request ordered channels
- **WHEN** the client requests `Oz,Fz,Pz`
- **THEN** all channel-indexed outputs preserve `Oz,Fz,Pz`

#### Scenario: Request the shared baseline from 1 through 50 Hz
- **WHEN** a compatible recording has sampling rate above 100 Hz and an algorithm requests output through 50 Hz
- **THEN** the shared spectral path returns available bins across the requested range
- **AND** preserves the algorithm's configured time and Welch policies.

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

The spectral loading boundary MUST accept independent filter and output frequency ranges. The preprocessing cache identity MUST include the effective filter range and notch setting, while output clipping MUST use only the output range. Both ranges SHALL be represented in provenance, and the algorithm window and Welch policy SHALL remain independently declared.

#### Scenario: Dynamic PSD uses configured filtering with a fixed trend axis
- **GIVEN** a dynamic PSD config with high-pass 0.5 Hz, low-pass 50 Hz, and notch 50 Hz
- **WHEN** a dynamic window is loaded
- **THEN** preprocessing uses 0.5–50 Hz and the explicitly selected 50 Hz notch
- **AND** returned frequency bins are clipped to the configured trend axis 1–50 Hz
- **AND** evidence records both filter and output ranges
