## ADDED Requirements

### Requirement: Optional subject demographics and historical age
Subjects SHALL support nullable sex (`male`, `female`, `other` or null). Sessions SHALL support nullable age-at-recording in whole years from 0 through 150. Subject changes and deletion SHALL NOT modify any Session age or signal identity. Omitted age in edits SHALL preserve it; explicit null SHALL clear it.

#### Scenario: Historical ages stay independent
- **WHEN** two recordings for the same subject have ages 25 and 26 and that subject is edited
- **THEN** both recordings keep their respective ages and scientific source bytes

#### Scenario: Leave demographic information empty
- **WHEN** sex and age are omitted during import or acquisition
- **THEN** both remain unknown and normal recording and analysis remain available

#### Scenario: Reject invalid demographic metadata
- **WHEN** sex is unsupported or age is fractional, negative or above 150
- **THEN** the request fails without changing recordings or associations

## MODIFIED Requirements

### Requirement: Keep local research hierarchy without PII
The system SHALL persist Project, Subject, Condition, and Session records in SQLite. Subjects SHALL support an optional project-unique local code and optional user-entered name; at least one SHALL be nonblank. Names SHALL NOT be used for automatic identity matching or written to application logs.

#### Scenario: Create a subject in a project
- **WHEN** a user supplies a code or name
- **THEN** the API returns a project-scoped Subject identity with trimmed metadata

#### Scenario: Reject a duplicate local code
- **WHEN** a second Subject uses the same nonblank local code in the same Project
- **THEN** the API returns a structured SUBJECT_CODE_CONFLICT error

### Requirement: Associate a recording through a session
The system SHALL associate a Recording with a Project through a Session referencing a Project, Recording, optional Subject and optional Condition. The source Recording SHALL remain immutable and globally identifiable.

#### Scenario: List a project session
- **WHEN** a user lists a Project's sessions
- **THEN** each Session returns its recording identity and nullable subject identity without copying source-file data

#### Scenario: Import without a subject
- **WHEN** neither code, name nor subject identity is supplied
- **THEN** import creates a Session with null subject identity and no placeholder Subject
