## MODIFIED Requirements

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
