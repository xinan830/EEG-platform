# Spec Delta

## Purpose

Let users bring external EDF/BDF recordings into a selected desktop project, associate them with a research Session, and continue into the established review workflow.

## ADDED Requirements

### Requirement: Import an EDF or BDF recording from the selected project
The desktop SHALL let a user select one EDF or BDF file while a project is selected and import it as an immutable recording with a generated recording identity.

#### Scenario: Import a readable file
- **WHEN** a user selects a readable EDF or BDF file from a selected project and confirms import
- **THEN** the system SHALL import the file and show its source filename, format, sampling frequency, duration, and ordered channel labels before completion

### Requirement: Associate an imported recording through a Session
The desktop SHALL associate each imported recording with the selected project through a newly created Session and a project-scoped non-identifying subject code.

#### Scenario: Use an existing subject
- **WHEN** the user selects an existing subject in the selected project and confirms import
- **THEN** the system SHALL create one Session referencing that subject and the imported recording

#### Scenario: Create a subject during import
- **WHEN** the user enters a new local subject code for the selected project
- **THEN** the system SHALL create the subject and one Session for the imported recording without collecting personal identifying information

### Requirement: Make imported recordings available in project review
After a successful import, the desktop SHALL show the recording under its selected project and SHALL allow opening it in the existing review workspace.

#### Scenario: Open an imported recording
- **WHEN** the user chooses review for a successfully imported recording
- **THEN** the review workspace SHALL open that recording and retain the owning project context when returning

### Requirement: Edit imported recording display names
The desktop SHALL enable the recording rename action for imported EDF/BDF entries, persist the name through the project Session API, and display the saved name after refresh.

#### Scenario: Save a project recording name
- **WHEN** a user saves a valid new name for an imported project recording
- **THEN** the project list SHALL show the persisted Session label after refresh
- **AND** a failed save SHALL retain the previous displayed name

### Requirement: Fail or cancel import without partial project records
Import SHALL support cancellation and SHALL publish the recording and Session association only after the source file and metadata have been validated successfully.

#### Scenario: Cancel during transfer
- **WHEN** the user cancels an import before completion
- **THEN** the operation SHALL stop, remove temporary data, and leave no recording or Session association

#### Scenario: Reject invalid input
- **WHEN** the selected file is unsupported, unreadable, structurally invalid, or has invalid channel metadata
- **THEN** the desktop SHALL show a readable error and leave no partial recording or Session association
