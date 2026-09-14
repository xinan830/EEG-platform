# data/research-projects Specification

## Purpose
TBD - created by archiving change add-projects-and-batch-runs. Update Purpose after archive.
## Requirements
### Requirement: Keep local research hierarchy without PII

The system SHALL persist Project, Subject, Condition, and Session records in
SQLite. A Subject SHALL use an internal `local_code` unique within a Project;
the Project API SHALL NOT accept personal names, birthdates, addresses, or
other identifying fields.

#### Scenario: Create a subject in a project
- **WHEN** a local user creates a subject with a unique local code for a project
- **THEN** the API returns its project-scoped Subject identity without PII

#### Scenario: Reject a duplicate local code
- **WHEN** a second Subject uses the same local code in the same Project
- **THEN** the API returns a structured `SUBJECT_CODE_CONFLICT` error

### Requirement: Associate a recording through a session

The system SHALL associate a Recording with a Project through a Session that
references a Project, Subject, Recording, and optional Condition. The source
Recording SHALL remain immutable and globally identifiable.

#### Scenario: List a project session
- **WHEN** a user lists a Project's sessions
- **THEN** each session returns its recording identity, local subject code, and
  optional condition without copying source-file data
