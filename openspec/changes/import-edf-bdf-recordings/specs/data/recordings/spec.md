# Spec Delta

## MODIFIED Requirements

### Requirement: Import supported EEG files
The system SHALL accept non-empty EDF and BDF files, store each validated source under an internal recording identifier, and reject unsupported or unreadable files without retaining partial source or metadata. Project-scoped imports SHALL associate the validated recording with the requested Session only after import succeeds. File transfer and validation SHALL use bounded memory and SHALL support cancellation.

#### Scenario: Import a readable recording
- **WHEN** a client uses the existing standalone import request with a readable EDF or BDF file
- **THEN** the system SHALL preserve the existing response contract and return the recording identifier, original base filename, extension, sampling frequency, duration, and file channel labels

#### Scenario: Import a readable project recording
- **WHEN** the desktop imports a readable EDF or BDF file for a valid project Session
- **THEN** the system SHALL store the original source bytes under an internal recording identifier
- **AND** it SHALL associate that recording identifier with the requested Session
- **AND** it SHALL return ordered channel labels and explicit sampling rate, duration, channel types, and channel units

#### Scenario: Reject an unreadable recording
- **WHEN** metadata cannot be read from the uploaded file
- **THEN** the system SHALL return a structured validation failure and remove temporary data, any partially stored file, metadata row, and uncommitted Session association

#### Scenario: Cancel a project import
- **WHEN** the client cancels or disconnects during transfer or validation
- **THEN** the system SHALL stop at a bounded checkpoint, release file handles, remove temporary data, and leave no partial recording or Session association
