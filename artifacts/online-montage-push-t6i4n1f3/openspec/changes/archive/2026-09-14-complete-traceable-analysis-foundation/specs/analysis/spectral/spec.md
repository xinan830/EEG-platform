## ADDED Requirements

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
