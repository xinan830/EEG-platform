## Purpose
Provide consistent optional subject code and name entry for acquisition, import, record editing and project recording lists without modifying scientific signals.

## ADDED Requirements

### Requirement: Shared demographic entry
All three entry points SHALL reuse the shared selector for optional sex and this recording's age in years. Selecting an existing subject SHALL load its sex without copying age from other recordings. Editing or deleting a person SHALL preserve historical ages; acquisition SHALL reset age for a new preparation. Unknown values SHALL remain empty, and no personal metadata SHALL affect scientific computation or default exports.

#### Scenario: Reuse subject with a new recording age
- **WHEN** an operator selects an existing person and enters age 26 for a new recording
- **THEN** the person's sex is shown and prior recordings keep their original age

### Requirement: Optional subject entry
Acquisition preparation, EDF/BDF import and record editing SHALL use the same project-scoped subject selector with unassociated, existing and new choices. New code and name SHALL be independently optional. Names alone SHALL NOT merge people.

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

### Requirement: Manage project subjects
The shared selector SHALL permit editing and deleting existing subjects. Updates SHALL preserve subject identity, reject cross-project access and duplicate codes, and refresh associated records. Deletion SHALL require confirmation, clear associations and preserve all signal files, records and analysis results.

#### Scenario: Reuse a name-only subject
- **WHEN** an existing name-only subject is selected during acquisition or record editing
- **THEN** the resulting record refers to that subject identity without creating a duplicate person

#### Scenario: Edit subject code
- **WHEN** the user edits a subject code or name and saves
- **THEN** records bound by that subject identity show the edited information after refresh

#### Scenario: Delete a subject
- **WHEN** the user confirms deletion of an existing subject
- **THEN** the subject disappears from selectors and associated records become unassociated without deleting EEG data
