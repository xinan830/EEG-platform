# Spectrogram baseline

## Purpose

Lock the current spectrogram time-axis, matrix, quality, unit, and display contract separately from the underlying spectral algorithm version.
## Requirements
### Requirement: Separate analysis and spectrogram versions
The API SHALL expose `analysis_algorithm_version = spectral-window-v1` and `spectrogram_contract_version = spectrogram-v3-bounded` as separate version concepts. Current STFT SHALL use bounded finite-context preprocessing rather than whole-record preparation: target sample range plus `ceil(max(2, 3 / low_hz) * sfreq)` samples per side, clamped to the source. It SHALL preserve notch-then-bandpass order, zero-phase filtering and existing quality rules, and report read/target bounds and context. Runtime current identities SHALL invalidate prior scientific cache reuse without converting historical results.

#### Scenario: Change spectrogram serialization

- **WHEN** matrix or time-coordinate semantics change without changing preprocessing or PSD mathematics
- **THEN** the spectrogram contract version changes independently of the analysis algorithm version

#### Scenario: First window in a long recording
- **WHEN** a short STFT request is submitted for a long recording
- **THEN** preparation work depends on target length, configured filters and context rather than total recording duration
- **AND** the service does not wait for whole-record scientific preparation

#### Scenario: Change preprocessing boundaries
- **WHEN** a bounded STFT result differs from historical whole-record filtering
- **THEN** the result carries the new scientific identity and explicit finite-context provenance
- **AND** it does not claim numerical equivalence to the old contract

### Requirement: Produce centered four-second time bins

The spectrogram SHALL use a 4-second demeaned Hann frame and a 1-second step. Each time coordinate SHALL be the center of its absolute source window.

#### Scenario: Analyze 10-40 seconds

- **WHEN** a 30-second range from `10.0 s` through `40.0 s` is analyzed
- **THEN** 27 rows represent windows `10-14` through `36-40` with centers `12` through `38 s`

### Requirement: Normalize one-sided STFT density for odd and even frame lengths

The STFT SHALL return a correctly normalized one-sided power spectral density in V^2/Hz. It SHALL double positive-frequency bins excluding DC; for an even frame length it SHALL leave the Nyquist bin undoubled, while for an odd frame length it SHALL double every bin after DC because no Nyquist bin exists.

#### Scenario: Normalize an odd-length frame
- **WHEN** an STFT frame has an odd number of samples
- **THEN** every returned positive-frequency density bin, including the last bin, is doubled while DC remains unchanged

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

### Requirement: Supplementary bands must not invalidate a valid output range
The spectrogram service SHALL compute supplementary standard-band integrals on
the uncropped grid of the selected filtered signal before masking the returned
matrix to the requested frequency range. It SHALL return null for bands whose
full edges are not supported by the recording frequency grid, without clipping
or extrapolating their edges.

#### Scenario: Request an Alpha-only STFT
- **WHEN** a static or dynamic Run requests 8-13 Hz with valid input quality
- **THEN** it completes with frequencies confined to 8-13 Hz
- **AND** supplementary Delta, Theta or Beta integration does not fail the Run

#### Scenario: Recording cannot support a standard band
- **WHEN** recording Nyquist is below the full Beta band but the requested
  output range is valid
- **THEN** that supplementary band is unavailable rather than zero or a Run error

### Requirement: Dynamic failure axes match the requested STFT range
The STFT module SHALL build exception-path frequency axes with the same frame
length and requested frequency bounds as successful windows.

#### Scenario: Initial quality failure followed by a clean window
- **WHEN** the first 8-13 Hz dynamic window fails the quality gate and a later
  window succeeds
- **THEN** both windows use the same frequency grid, rejected values remain
  unavailable, and the later matrix is preserved
