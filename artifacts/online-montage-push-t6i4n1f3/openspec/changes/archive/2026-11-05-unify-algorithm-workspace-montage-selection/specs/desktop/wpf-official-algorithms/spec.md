## MODIFIED Requirements

### Requirement: Standalone algorithm requests use one captured montage snapshot

The standalone algorithm workspace SHALL submit official Runs and preview requests using one captured `WorkspaceMontageSelectionSnapshot`, including montage ID, revision, fingerprint, and resolved ordered output mappings. It SHALL not combine a montage value from one state owner with channels from another.

#### Scenario: Submit a Run after changing montage

- **WHEN** the user changes the algorithm workspace montage before submission
- **THEN** the request contains the current montage snapshot and its resolved outputs
- **AND** no previous montage output remains an active input

#### Scenario: Selection changes while a request is prepared

- **WHEN** the selection revision changes before request submission completes
- **THEN** the request is discarded or rebuilt from one current snapshot
- **AND** a mixed-revision request is never submitted
