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

#### Scenario: Restore exported channel identity with an unlabeled disabled input
- **WHEN** an exported EDF/BDF sidecar maps a physical input whose channel configuration is disabled and has no electrode label
- **THEN** the system SHALL accept the valid physical index and kind mapping
- **AND** it SHALL retain the EDF/BDF channel label for that input while restoring configured electrode labels for labeled inputs
- **AND** it SHALL continue rejecting duplicate or invalid physical indexes, display orders, kinds, hashes, or signal labels

#### Scenario: Restore verified channel and montage provenance
- **WHEN** a project import includes an export sidecar whose signal hash matches the uploaded EDF/BDF
- **THEN** the system SHALL validate and persist the channel configuration snapshot, source-column-to-native-channel mapping, acquisition montage snapshot, and montage configuration snapshot together
- **AND** it SHALL preserve the EDF/BDF source column order and source bytes unchanged
- **AND** later recording metadata reads SHALL expose the verified snapshots needed by review and algorithm input resolution

#### Scenario: Reject an invalid provenance envelope
- **WHEN** a sidecar is present but its hash, channel mapping, or montage snapshot is malformed or inconsistent
- **THEN** the system SHALL return a structured import error and SHALL NOT publish the source file, recording row, or Session association

#### Scenario: Import without a sidecar
- **WHEN** an EDF/BDF file has no supported export sidecar
- **THEN** the system SHALL import it as an ordinary source recording
- **AND** it SHALL NOT claim that channel configuration or montage provenance was restored
