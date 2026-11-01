## ADDED Requirements

### Requirement: Preserve standalone RBP outcomes across execution strategies

Standalone RBP SHALL use the same scientific frame calculation and channel failure rules for static ranges, full dynamic frames and on-demand frames. For identical source, ordered channels, parameters and recording-absolute half-open sample bounds, these strategies SHALL return identical five-band ratio arrays, channel quality reasons and spectral/preprocessing evidence. Full dynamic quality SHALL reflect actual window outcomes instead of reporting clean when any window is unavailable or has warnings. Public result shapes and canonical time planning SHALL remain unchanged. The module SHALL use public runtime scheduling, cancellation and compute admission without a private task queue or a synthetic TNK group. Scientific version v4 SHALL retain finite artifact-bearing segments with channel-specific warnings; it SHALL reject missing/non-finite data and invalid total power per channel without imputation. Other algorithms SHALL retain their declared quality rules.

#### Scenario: Compare a multi-channel frame across strategies
- **WHEN** static, full and on-demand requests evaluate the same sample window and ordered source channels
- **THEN** five-band values, ratio units, channel order and per-channel failure details match
- **AND** source PSD provenance and the existing continuous 1–50 Hz preprocessing with 50 Hz notch Q=30 remain traceable

#### Scenario: Validate an explicitly ordered channel set
- **WHEN** standalone RBP declares an ordered channel-set input and returns multiple channels
- **THEN** public validation accepts the declared variable channel count only when result order and array channel dimensions match the requested unique channels
- **AND** algorithms declaring fixed roles retain their exact role-count validation

#### Scenario: Preserve a failed channel without inventing measurements
- **WHEN** one selected channel has missing/non-finite samples or an invalid denominator
- **THEN** unavailable band shares remain null with structured channel failure reasons in every strategy
- **AND** unaffected channels remain usable and a full Run containing failed frames does not claim clean overall quality

#### Scenario: Annotate artifacts without suppressing computable RBP
- **WHEN** one channel exceeds the amplitude threshold or shows suspected clipping or low amplitude, while its samples and five-band denominator remain valid
- **THEN** v4 retains that channel's measured ratios and labels its quality warnings
- **AND** healthy channel values and quality do not change when that channel is included
- **AND** the RBP interface explains that retained values may include artifacts

#### Scenario: Cancel a standalone RBP target
- **WHEN** public runtime cancellation is observed before input preparation or after preparation before calculation
- **THEN** execution stops without publishing a successful target
- **AND** resource ownership and task lifecycle remain with the public execution channel
