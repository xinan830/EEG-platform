# Spec Delta

## MODIFIED Requirements

### Requirement: Associate a recording through a session
The system SHALL associate a Recording with a Project through a Session that references a Project, Subject, Recording, and optional Condition. The source Recording SHALL remain immutable and globally identifiable. A desktop project SHALL resolve to one stable backend Project identity before an imported recording is associated.

#### Scenario: List a project session
- **WHEN** a user lists a Project's sessions
- **THEN** each session returns its recording identity, local subject code, and optional condition without copying source-file data

#### Scenario: Associate a desktop import
- **WHEN** a desktop import targets a project and a valid existing or newly created project-scoped subject
- **THEN** the system SHALL create a Session that references the stable backend Project identity, Subject identity, and imported Recording identity
- **AND** listing that project's sessions SHALL return the imported recording association

#### Scenario: Reject a mismatched project identity
- **WHEN** an import references a Subject or Session belonging to another Project
- **THEN** the system SHALL reject the association with a structured error and SHALL NOT create a cross-project Session

## ADDED Requirements

### Requirement: Resolve desktop project identity consistently
The backend SHALL resolve a desktop project's stable identifier to the same backend Project on repeated import operations, without creating duplicate projects for that desktop project.

#### Scenario: Import repeatedly from one desktop project
- **WHEN** the desktop resolves the same project identifier for multiple recording imports
- **THEN** each import SHALL use one backend Project identity and SHALL NOT create duplicate Project rows
