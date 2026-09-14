# Spectrogram baseline

## Purpose

Lock the current spectrogram time-axis, matrix, quality, unit, and display contract separately from the underlying spectral algorithm version.

## Requirements

### Requirement: Separate analysis and spectrogram versions

The API SHALL expose `analysis_algorithm_version = offline-spectral-v3` and `spectrogram_contract_version = spectrogram-v2` as separate version concepts.

#### Scenario: Change spectrogram serialization

- **WHEN** matrix or time-coordinate semantics change without changing preprocessing or PSD mathematics
- **THEN** the spectrogram contract version changes independently of the analysis algorithm version

### Requirement: Produce centered four-second time bins

The spectrogram SHALL use a 4-second demeaned Hann frame and a 1-second step. Each time coordinate SHALL be the center of its absolute source window.

#### Scenario: Analyze 10-40 seconds

- **WHEN** a 30-second range from `10.0 s` through `40.0 s` is analyzed
- **THEN** 27 rows represent windows `10-14` through `36-40` with centers `12` through `38 s`

### Requirement: Preserve matrix alignment across bad windows

The spectrogram SHALL keep every valid time-bin coordinate. A bad window SHALL retain its row and quality metadata while unavailable power cells are non-values, not zeros.

#### Scenario: Encounter one bad frame

- **WHEN** a frame fails a quality rule
- **THEN** its center remains in the time axis and later rows do not shift left

### Requirement: Expose linear and display power explicitly

The backend SHALL provide linear density in `uV^2/Hz` and display density in `dB re 1 uV^2/Hz`, with `10*log10(P/(1 uV^2/Hz))` performed in the backend.

#### Scenario: Render a heatmap

- **WHEN** the frontend selects linear or dB display
- **THEN** it renders the corresponding backend matrix and does not recompute EEG power

### Requirement: Keep display selection separate from matrix precision

The backend response SHALL retain the complete frequency grid while the frontend MAY display a band preset or custom 1-30 Hz subrange and sparse axis labels.

#### Scenario: Select Alpha display

- **WHEN** the user selects 8-13 Hz
- **THEN** the chart displays only that range without modifying backend spectral values
