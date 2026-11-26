## ADDED Requirements

### Requirement: Built-in profile display names follow product branding
Built-in channel and montage profiles SHALL display POP through shared presentation ownership. Classification and deduplication SHALL use source, stable preset identity and signal structure rather than display names. Historical snapshots, serialized names, fingerprints and user-owned names SHALL NOT be rewritten. Units, sample times, channel order, formulas and quality semantics SHALL remain unchanged.

#### Scenario: An existing built-in snapshot is opened
- **WHEN** a historical system preset is displayed in channel, montage, acquisition or review UI
- **THEN** its product name is POP while its serialized snapshot and fingerprints remain unchanged

#### Scenario: Equivalent system presets have different display names
- **WHEN** system presets with equivalent signal semantics have different names
- **THEN** deduplication retains the first equivalent preset without name-prefix checks

#### Scenario: A user chooses an ANT-containing name
- **WHEN** a user-owned profile contains ANT in its name
- **THEN** its name is displayed verbatim and does not cause it to be classified as a built-in preset
