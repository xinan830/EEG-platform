## MODIFIED Requirements

### Requirement: WPF submits a traceable offline Run

The WPF workspace SHALL submit an official Run only for a successfully registered Recording and SHALL include the selected algorithm ID, exact scientific version, explicit source channel roles, the active workspace montage identity and immutable snapshot fingerprint, exact resolved analysis output channels and their order, requested range, mode, and validated parameter values. The montage identity and output mapping SHALL be part of scientific request identity. The Run SHALL use the same montage selection revision observed by the review workspace; it SHALL NOT infer or maintain an independent montage selection. Explicit current compatible profiles SHALL be validated and persisted as immutable snapshots before selection; acquisition versus analysis-snapshot provenance SHALL remain distinguishable.

#### Scenario: Submit a Run using the current review-workspace selection
- **WHEN** a user submits a runnable official algorithm with valid roles and a valid range
- **THEN** the request contains the active workspace montage snapshot and its resolved output channels
- **AND** the request does not calculate scientific values in WPF

#### Scenario: Static Run is submitted
- **WHEN** the user selects a runnable official algorithm, valid roles, a valid range, and a workspace montage
- **THEN** WPF creates one Run with the selected montage snapshot and resolved ordered outputs
- **AND** WPF performs no scientific calculation locally

#### Scenario: Montage changes before Run submission
- **WHEN** the user changes the workspace montage from REF to AV before submitting
- **THEN** the request contains the AV snapshot and corresponding resolved outputs
- **AND** no prior REF output remains as an active input

#### Scenario: Montage changes before submission
- **WHEN** the user changes the shared workspace montage from REF to AV before submitting
- **THEN** the request contains AV and its corresponding resolved outputs
- **AND** the prior REF outputs are absent from active inputs

#### Scenario: Montage changes while a Run request is being prepared
- **WHEN** the workspace selection revision changes before submission is committed
- **THEN** WPF discards or rebuilds the prepared request against the latest revision
- **AND** it does not submit a mixture of montage identity and channel mappings from different revisions

#### Scenario: The review selection changes while a request is prepared

- **WHEN** the workspace montage revision changes before request submission is committed
- **THEN** WPF discards or rebuilds the prepared request from one latest selection snapshot
- **AND** it does not submit montage identity and channel mappings captured from different revisions

#### Scenario: A selected review montage cannot resolve a required role
- **WHEN** the workspace-selected montage cannot resolve one or more required algorithm roles
- **THEN** WPF refuses submission with a structured mapping error
- **AND** no Run is created

#### Scenario: Montage mapping is unavailable
- **WHEN** the selected workspace montage cannot resolve one of the recorded source roles
- **THEN** WPF refuses submission with a structured mapping error
- **AND** no Run is created
