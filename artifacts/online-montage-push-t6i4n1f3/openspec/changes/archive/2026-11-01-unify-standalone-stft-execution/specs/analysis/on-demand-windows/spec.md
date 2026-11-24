## MODIFIED Requirements

### Requirement: Preserve scientific output and distinguish lifecycle states

The service SHALL distinguish source preparation, queued work, window computation, ready scientific output, insufficient data, rejected/unavailable science, cancellation, and execution failure. Missing scientific values SHALL be null with structured reasons. A ready window SHALL match the same window from the existing full dynamic Run under the declared algorithm-specific tolerance and SHALL retain the declared versioned algorithm-specific preprocessing contract.

#### Scenario: PSD window equivalence

- **WHEN** a PSD window and its matching full dynamic Run window use identical source bytes, configuration, identity, and scientific contract
- **THEN** their sample bounds, frequency axes, values, units, quality/evidence, and preprocessing provenance match within the declared tolerance
- **AND** the implementation does not locally filter only the requested short window to avoid continuous zero-phase preparation

#### Scenario: RBP window equivalence

- **WHEN** an opted-in RBP window and its full dynamic Run use identical source bytes, acquisition/analysis reference, channel, definition, configuration and window policy
- **THEN** the five named Delta/Theta/Alpha/Beta/Gamma ratios, actual sample range, quality and full window evidence match within `rtol=1e-12, atol=1e-12`
- **AND** both paths use RBP's existing continuous 1–50 Hz preprocessing and 50 Hz notch before slicing, without borrowing PSD's configurable filter range
- **AND** unavailable denominators, gaps and scientific quality rejection retain the existing RBP failure contract without zero shares

#### Scenario: Per-algorithm opt-in stays disabled until final acceptance

- **WHEN** additional algorithms pass their independent automated scientific and module validation during the user-approved migration phase
- **THEN** they may declare their window capability behind independent default-off backend and WPF flags
- **AND** GUI playback, parameters, failure visibility and rollback remain explicit final acceptance work, not implied by successful automated tests

#### Scenario: Scalar spectral window equivalence

- **WHEN** Band Ratio, Peak Frequency, IAPF or Theta/Beta executes an opted-in single window with identical source and scientific configuration to a full dynamic Run
- **THEN** its value, actual range, quality, failure and evidence match the corresponding full Run point within `rtol=1e-12, atol=1e-12`
- **AND** both paths retain continuous preprocessing before slicing and use the configured window length to identify Partial versus Complete
- **AND** a missing value is never emitted as zero; Theta/Beta preserves its historical `dimensionless` unit

#### Scenario: Scientific rejection differs from execution failure

- **WHEN** the requested samples fail a scientific quality rule
- **THEN** the service publishes/returns the algorithm's rejected or unavailable state and structured quantitative evidence
- **AND** it does not report the quality outcome as a fabricated success or generic infrastructure failure

#### Scenario: STFT window equivalence with bounded preparation

- **WHEN** current STFT static, full dynamic and on-demand execution use identical source bytes, configuration and target sample bounds
- **THEN** they prepare only target samples plus `ceil(max(2, 3 / low_hz) * sfreq)` context samples per side, clamped to source boundaries
- **AND** their values, units, axes and finite-context provenance match under `spectrogram-v3-bounded` without using a whole-record scientific cache
- **AND** old whole-record STFT outputs cannot seed current window reuse; different outer sample bounds are not claimed numerically equivalent
