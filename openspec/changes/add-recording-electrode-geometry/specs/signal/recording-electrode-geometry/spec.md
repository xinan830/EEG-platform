## Purpose
Bind source electrode geometry to a recording so spatial preprocessing and visual review use the same verified positions without guessing physical sites from overlapping labels.

## ADDED Requirements

### Requirement: Validate and preserve recording geometry
The system SHALL preserve optional source-bound three-dimensional electrode geometry through import, storage and export. Coordinates MUST declare meters, head frame, source and transformation provenance, and unique native-channel bindings. Invalid supplied geometry MUST fail explicitly; geometry MUST NOT change sampling time, units or channel order.

#### Scenario: Import a custom array
- **WHEN** valid recording geometry identifies an electrode named A1 at a custom site
- **THEN** that location is preserved instead of substituting a standard site named A1

#### Scenario: Reject contradictory geometry
- **WHEN** geometry contains nonfinite or duplicate positions, incorrect channel bindings or unsupported units or frame
- **THEN** import fails with an actionable structured error before publishing a recording

### Requirement: Share geometry among scientific spatial consumers
Bad-channel spatial eligibility and interpolation, ICA classification and ICA scalp maps SHALL consume the same recording geometry. Supplied custom geometry MUST NOT be mixed with standard-template guesses. Changing geometry MUST invalidate affected scientific and ICA cache identities. Diagram-only coordinates MUST NOT be used as scientific three-dimensional positions.

#### Scenario: Apply custom spatial preprocessing
- **WHEN** a CAR recording with verified custom geometry is inspected, interpolated or classified
- **THEN** each spatial consumer uses its bound source positions and retains normal reference and quality gates

#### Scenario: Missing custom electrode
- **WHEN** a requested electrode is absent from supplied custom geometry
- **THEN** its spatial operation reports unavailable without standard-name fallback

### Requirement: Preserve nonspatial compatibility and channel roles
The system SHALL retain existing behavior for recordings without custom geometry and SHALL NOT infer physiological identities from geometry or EXG names. No component SHALL be removed merely because coordinates enable classification.

#### Scenario: Auxiliary identities remain unconfirmed
- **WHEN** custom geometry is imported alongside EXG channels of undocumented purpose
- **THEN** these channels retain their recorded identities and are not automatically assigned EOG or ECG roles
