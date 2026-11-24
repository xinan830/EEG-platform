# analysis/standalone-stft-execution Specification

## Purpose
TBD - created by archiving change unify-standalone-stft-execution. Update Purpose after archive.
## Requirements
### Requirement: One standalone STFT frame execution
Standalone STFT MUST route static, full dynamic and on-demand computation through one module-owned typed frame outcome using the existing unified execution channel. It MUST preserve the selected source channel, 4 s Hann segments, 1 s inner steps and requested frequency/notch settings. Current science MUST be `spectrogram-v3-bounded`, reading target samples plus `ceil(max(2, 3 / low_hz) * sfreq)` context samples per side, clamped to recording boundaries, then zero-phase filtering and cropping. Linear outputs MUST be V^2/Hz; dB outputs MUST be dB re 1 uV^2/Hz.

#### Scenario: Same complete window through three strategies
- **WHEN** static, uncached full dynamic and on-demand execute the same source/configuration and sample bounds
- **THEN** their usable matrices, frequency axis, source evidence and source request settings agree
- **AND** static centers are recording-absolute while dynamic inner centers are relative to the actual source window

#### Scenario: First window of a 24-hour recording
- **WHEN** a five-second STFT window is requested from a 24-hour recording
- **THEN** source reads and filtering are limited to that target and declared context with selected source channels
- **AND** neither whole-record preparation nor a whole-record scientific-cache readiness barrier is invoked
- **AND** result evidence includes target/read sample bounds, context, boundary policy and current scientific version

#### Scenario: Finite filter boundary changes
- **WHEN** finite-context results differ from prior whole-record preprocessing at boundaries
- **THEN** current identities and provenance distinguish the new contract without claiming numerical equivalence or silently reusing old results

### Requirement: Shared validation and explicit failed windows
The frame boundary MUST validate time/frequency coordinates, matrix shapes and usable finite nonnegative linear power. Static partial output MAY retain good inner rows with explicit bad-row evidence. Both dynamic strategies MUST reject an outer window containing any bad inner row. An unavailable or rejected dynamic window MUST NOT publish finite values or shift later time coordinates.

#### Scenario: Bad inner row
- **WHEN** a source frame contains an explicitly rejected inner row
- **THEN** static output retains available rows and their quality evidence
- **AND** full dynamic marks its corresponding outer slot rejected with NaN matrices and on-demand returns rejected with no matrices, both with the same failure code and source evidence

#### Scenario: Malformed source matrix
- **WHEN** source shapes, coordinates or unmarked nonfinite values violate the frame contract
- **THEN** all strategies report structured unavailability rather than valid science
- **AND** a later valid dynamic frame remains independently computable on the correct frequency grid

### Requirement: Reuse platform lifecycle and cache ownership
STFT MUST reuse platform admission, cancellation, planning, persistence and verified complete-window reuse. It MUST perform at most one spectrogram source load per uncached frame, propagate cancellation, and distinguish the new execution identity without compatibility executors or rewriting existing artifacts.

#### Scenario: Cancel during source preparation
- **WHEN** cancellation occurs after a source load before publication
- **THEN** no successful frame is published and cancellation propagates to the owning execution service

#### Scenario: Warmup window
- **WHEN** a dynamic window has only one complete inner segment
- **THEN** on-demand returns only the actual relative center and matrix row with Partial provenance
- **AND** full dynamic adds only explicitly unavailable NaN padding to its declared inner grid
