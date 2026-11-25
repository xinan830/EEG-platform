## Purpose
Provide consistent optional subject code and name entry for acquisition, import, record editing and project recording lists without modifying scientific signals.

## ADDED Requirements

### Requirement: Optional subject entry
Acquisition preparation and EDF/BDF import SHALL accept optional subject code and name independently. Both blank SHALL mean unassociated. Names alone SHALL NOT merge people.

#### Scenario: Start acquisition without personal metadata
- **WHEN** both subject fields are blank
- **THEN** acquisition and recording remain available without a generated subject code

#### Scenario: Import with a name only
- **WHEN** a user supplies a name and no code
- **THEN** import succeeds and preserves that name as local management metadata

### Requirement: Edit and display subject metadata
Record editing SHALL support code, name and clearing the association for local and imported records. Project rows SHALL display code and name or 未关联. Reusing an existing code SHALL reference that subject; shared name edits SHALL be explicitly described in the editor and reflected after refresh.

#### Scenario: Update record metadata
- **WHEN** a user saves an edited record name and optional subject information
- **THEN** the list and subsequent reload show the saved values while source bytes, time, units, channel order, montage and algorithm results remain unchanged

#### Scenario: Clear subject
- **WHEN** the user clears both fields and saves
- **THEN** the record becomes unassociated without deleting other records or subjects
