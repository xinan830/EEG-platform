## ADDED Requirements

### Requirement: Provide deliberate semantic channel mapping

The system SHALL distinguish source channel labels from persisted semantic
roles. It SHALL expose a normal-workbench flow to inspect and save the Fz/Pz/Oz
mapping without blocking waveform viewing.

#### Scenario: Confirm an O2 posterior channel

- **WHEN** a recording contains Fz, Pz, and O2 but no exact Oz label
- **THEN** the mapping editor MAY suggest O2 for the Oz role
- **AND THEN** it saves that role only after an explicit user confirmation

#### Scenario: View without a mapping

- **WHEN** a recording has no persisted semantic mapping
- **THEN** the user can still select raw channels and view the waveform

### Requirement: Restrict automatic import mapping to exact labels

The importer SHALL automatically persist a mapping only when Fz, Pz, and Oz
each have exactly one case/whitespace-normalized source-label match. It SHALL
not infer semantic roles from aliases or source order.

#### Scenario: Exact source labels

- **WHEN** a source file contains exact logical labels Fz, Pz, and Oz
- **THEN** the importer persists the matching mapping

#### Scenario: O2 is not Oz

- **WHEN** a source file contains O2 but lacks Oz
- **THEN** the importer does not persist an Oz mapping automatically
