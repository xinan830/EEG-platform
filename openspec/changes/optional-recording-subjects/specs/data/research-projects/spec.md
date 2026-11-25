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
