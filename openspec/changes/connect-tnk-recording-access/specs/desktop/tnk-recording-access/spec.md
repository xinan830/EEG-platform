# desktop/tnk-recording-access Specification

## ADDED Requirements

### Requirement: Workspace owns one recording access manager

The desktop workspace MUST own one `RecordingSessionManager`, and Review and TNK MUST obtain recording leases from that manager. Consumers MUST NOT construct a second production manager for the same workspace.

#### Scenario: Review and TNK select the same recording

- **WHEN** both consumers open the same local recording
- **THEN** the manager reuses one indexed source lifetime and returns independent leases

### Requirement: TNK validates the shared source before registration

Before registering a selected local recording with the backend, the TNK workbench MUST acquire a lease and validate the recording identity, V source unit, positive sampling rate, sample bounds, and non-empty ordered source channels. A failed validation MUST prevent registration and publish a structured user-visible error.

#### Scenario: Invalid source is rejected before backend registration

- **WHEN** the selected recording has an invalid source unit or sample range
- **THEN** TNK reports the recording-access error and does not send the registration request

### Requirement: Scientific execution stays in the backend

The desktop TNK lease MUST NOT perform filtering, feature extraction, or algorithm calculation. Dynamic TNK windows continue to use the backend recording access service until an explicit sample transport contract is approved.

#### Scenario: Dynamic TNK remains backend-scientific

- **WHEN** TNK requests a dynamic analysis window
- **THEN** the backend reads the registered recording through its recording-access service and owns all scientific computation
