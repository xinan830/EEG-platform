## MODIFIED Requirements

### Requirement: Separate display montage from analysis reference

The system SHALL keep the review display montage, acquisition montage, and algorithm analysis
montage as explicit separate concepts. The algorithm analysis montage SHALL be selected from
the immutable acquisition catalogue or an explicitly submitted, backend-validated, persisted
analysis snapshot bound to recorded channels, and SHALL be included in scientific request identity.
The system SHALL NOT infer the scientific analysis reference from a waveform display montage.

#### Scenario: Select an average display montage

- **WHEN** the user changes the viewer montage
- **THEN** the fixed algorithm analysis montage remains unchanged
- **AND** no official algorithm Run is reused or recomputed solely because of the display change

#### Scenario: Select an analysis montage in the algorithm detail

- **WHEN** the user changes the algorithm analysis montage from REF to AV
- **THEN** the corresponding recorded source mapping is projected for analysis
- **AND** the analysis request and provenance identify AV
- **AND** the viewer display montage remains independent

