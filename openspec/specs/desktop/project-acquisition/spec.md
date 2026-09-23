# desktop/project-acquisition Specification

## Purpose
This capability defines project-owned EEG acquisition, preview, recording, and project data-preservation behavior.

## Requirements

### Requirement: Desktop projects own their acquisition records

The desktop SHALL require every acquisition session to belong to one persisted
research project. A project SHALL have a stable ID and number, user-facing
metadata, and a user-selected project directory. Raw sessions SHALL be created
only below that project's `recordings` directory.

The live acquisition workspace SHALL NOT be a global navigation destination.
The project action SHALL open a preparation page where the user selects a valid
montage and a device-supported sampling rate. The desktop SHALL open the device
stream in preview mode before navigating from preparation to the live
acquisition workspace. Preview mode SHALL NOT create a raw manifest, sample
chunk, or audit record. Raw persistence SHALL begin only after the user
explicitly starts recording.

#### Scenario: A user starts acquisition from a project

- **WHEN** the user selects a project, montage, and sampling rate and starts acquisition
- **THEN** the acquisition request SHALL carry the selected project identity and immutable snapshot
- **AND** the raw session SHALL be written below the selected project's `recordings` directory
- **AND** the raw manifest SHALL persist the project context separately from hardware configuration.

#### Scenario: A user previews before recording

- **WHEN** the user confirms a valid acquisition preparation
- **THEN** the desktop SHALL open and display the live EEG stream without creating a raw recording
- **AND** the recording elapsed time SHALL remain unset until the user starts recording
- **AND** starting recording SHALL create the project-owned raw session without reopening the device stream.

#### Scenario: A user finishes recording

- **WHEN** the user chooses record complete from preview, recording, or paused state
- **THEN** the desktop SHALL close the device stream and complete any active raw writer
- **AND** it SHALL return to the project list and refresh that project's recordings
- **AND** finishing preview before recording SHALL NOT create an empty recording.

#### Scenario: A user opens acquisition from a project

- **WHEN** the user selects a project and chooses its start-acquisition action
- **THEN** the desktop SHALL show the acquisition preparation page
- **AND** it SHALL NOT open an EEG stream until the user selects a valid montage and sampling rate and confirms start
- **AND** it SHALL show the live acquisition workspace only after stream opening succeeds.

#### Scenario: A user uses global navigation

- **WHEN** the user views the desktop global navigation
- **THEN** it SHALL NOT offer a direct live-acquisition destination
- **AND** it SHALL require acquisition to begin from a selected project.

#### Scenario: No project is selected

- **WHEN** the user reaches acquisition without selecting a project
- **THEN** starting acquisition SHALL be unavailable or fail with a readable preparation error
- **AND** no device stream or standalone recording directory SHALL be opened.

### Requirement: Project removal preserves research data

Removing a project from the workstation index SHALL NOT delete its project
directory, raw sessions, audits, or artifacts. A project directory with
existing acquisition records SHALL NOT be reassigned through project editing.

#### Scenario: A user removes a project

- **WHEN** the user confirms removal of an idle project
- **THEN** only the local project index entry SHALL be removed
- **AND** all files below the project directory SHALL remain unchanged.

#### Scenario: A user changes a populated project directory

- **WHEN** a project already has acquisition records and the user selects another directory
- **THEN** the save SHALL fail with a readable error
- **AND** the existing project and recording locations SHALL remain unchanged.
