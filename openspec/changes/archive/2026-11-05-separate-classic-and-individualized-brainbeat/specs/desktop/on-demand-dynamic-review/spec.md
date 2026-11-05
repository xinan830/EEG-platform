## ADDED Requirements

### Requirement: Review identifies the selected BrainBeat variant
The recording-review workspace SHALL allow users to select classic BrainBeat or the individualized IAPF-relative variant. It SHALL submit the selected official algorithm identity and catalog-declared dynamic window settings to the backend. The desktop SHALL display the returned variant label, actual analysis range, ratio unit, quality state, and failure reason without recomputing either formula.

#### Scenario: Select classic BrainBeat in review
- **WHEN** the user selects classic BrainBeat and requests the current cursor window
- **THEN** the request identifies classic `brainbeat` and uses the catalog-declared 20-second/one-second policy
- **AND** the result presentation identifies fixed-band absolute-power semantics

#### Scenario: Select individualized BrainBeat in review
- **WHEN** the user selects the individualized variant and requests the current cursor window
- **THEN** the request identifies `brainbeat_individualized` and uses its catalog-declared policy
- **AND** the result presentation identifies IAPF-relative semantics

#### Scenario: Show insufficient, rejected, or unavailable BrainBeat windows
- **WHEN** the selected BrainBeat result is not ready, fails quality, or has unavailable IAPF/bands
- **THEN** review shows the backend state and actual requested range
- **AND** it does not show a stale or fabricated numeric result
