## MODIFIED Requirements

### Requirement: Expose historical analysis montage choices

The recording-review workspace SHALL expose one montage selector backed by the recording's validated immutable montage catalogue. Every review algorithm panel SHALL consume the workspace selection and SHALL NOT expose a separate analysis-montage selector. Selection SHALL remain bound to that recording's channel snapshot and actual source signals; it SHALL NOT be replaced by a current global or unrelated workspace setting. Explicit compatible montage definitions SHALL be independently validated and persisted as immutable snapshots before becoming selectable.

#### Scenario: Register a compatible montage for a record
- **WHEN** the client submits a complete montage definition compatible with the record's channels
- **THEN** the backend validates device identity, channel configuration identity, physical mapping, hardware reference and ground, formula dependencies, and V units
- **AND** the valid definition is persisted as an immutable record-scoped snapshot whose identity changes when its content changes
- **AND** the workspace catalogue can reload that same snapshot and distinguish acquisition from analysis snapshot provenance
- **AND** the original manifest and sample files remain unchanged

#### Scenario: Register current compatible alternatives for a record without a saved catalogue
- **WHEN** the client explicitly submits a complete montage definition compatible with the record's channels
- **THEN** the backend independently validates device, channel-configuration identity, physical mapping, hardware reference and ground, formula dependencies, and V units
- **AND** it persists the accepted definition as an immutable record-scoped snapshot with a content-derived identity
- **AND** the workspace's single montage selector can reload the snapshot and its outputs
- **AND** provenance distinguishes acquisition and analysis snapshots while raw manifest and samples remain unchanged

#### Scenario: Open a record with a saved montage catalogue
- **WHEN** the user opens a completed record with a valid montage catalogue
- **THEN** the workspace selector initializes using the documented acquisition-default rule
- **AND** waveform display and algorithm panels observe the same selected montage identity
- **AND** the original recording data is not modified

#### Scenario: Open a record with a saved acquisition montage
- **WHEN** the user opens a completed record with a valid saved acquisition montage
- **THEN** the workspace's single montage selector is available and defaults to that acquisition montage
- **AND** waveform display and all algorithm panels use that selected snapshot
- **AND** original recording data is not modified

#### Scenario: Open a recording-review workspace

- **WHEN** the user opens a completed recording with a validated montage catalogue
- **THEN** the workspace initializes its single selection using the documented acquisition-default rule
- **AND** waveform display and every algorithm panel read the same montage snapshot
- **AND** the original recording data remains unchanged

#### Scenario: Montage catalogue is incomplete or incompatible
- **WHEN** the record lacks a valid montage catalogue or the selected snapshot does not match its recorded channels
- **THEN** the workspace reports a structured unavailable reason
- **AND** montage-dependent waveform or algorithm work cannot silently substitute another mapping

#### Scenario: Historical montage choices are incomplete
- **WHEN** the record lacks a montage catalogue, has ambiguous source mapping, or its snapshot disagrees with the recorded channel table
- **THEN** the workspace shows a structured unavailable reason
- **AND** dependent algorithm Runs are prevented without using a global or positional fallback

#### Scenario: Historical montage choices are inconsistent
- **WHEN** the record has an ambiguous source mapping or its montage snapshot does not match the recorded channel table
- **THEN** the workspace reports a structured unavailable reason
- **AND** montage-dependent algorithms cannot run using a global setting or positional channel guess

#### Scenario: The selected review montage is unavailable

- **WHEN** the recording lacks a valid montage catalogue or the current selection does not match its recorded channels
- **THEN** the review workspace reports a structured unavailable reason
- **AND** montage-dependent waveform and algorithm work cannot substitute another montage

### Requirement: Link montage configuration to analysis channels

Algorithm input channels SHALL be derived from the shared workspace montage selection, the recording's actual available outputs, and the algorithm's declared input roles. Algorithm panels MAY retain algorithm-specific role or channel-subset choices, but SHALL NOT retain a separate montage selection. A montage change SHALL remap each selected role through the new workspace snapshot.

#### Scenario: Switch the review montage while retaining algorithm roles
- **WHEN** the user changes the workspace montage from REF to AV
- **THEN** each algorithm's selected semantic roles resolve to corresponding AV outputs where available
- **AND** each panel's displayed inputs and submitted request reflect the same AV snapshot

#### Scenario: Switch REF to average reference
- **WHEN** selected roles correspond to channel 1 and channel 2 and the user changes the workspace selection from REF to AV
- **THEN** those roles resolve to channel 1-AV and channel 2-AV where available
- **AND** displayed algorithm inputs match the submitted request

#### Scenario: Selected role is unavailable in the shared montage
- **WHEN** the selected montage cannot generate an algorithm's required role
- **THEN** that role is unavailable with a structured reason
- **AND** the algorithm does not retain an old-montage input or substitute another channel

#### Scenario: Selected role is unavailable in a montage
- **WHEN** the selected montage cannot generate a currently selected algorithm role
- **THEN** the role is unavailable with a structured missing-source or incompatibility reason
- **AND** another channel or the old montage output is not used

#### Scenario: A selected review role is unavailable

- **WHEN** the workspace-selected snapshot cannot provide a required algorithm role
- **THEN** the role is unavailable with a structured reason
- **AND** the algorithm does not retain an old-snapshot input or substitute another channel

### Requirement: Recalculate official algorithms after workspace montage change

A workspace montage change SHALL create a new scientific configuration revision and trigger the applicable official algorithm refresh workflow. Static, dynamic full, on-demand, and combined execution paths SHALL use the same montage identity and stale-result rules.

#### Scenario: Change montage when no run is active
- **WHEN** the user changes the workspace montage from REF to AV
- **THEN** each algorithm panel's current request configuration resolves to AV
- **AND** outputs from REF are cleared or marked stale
- **AND** new calculations use AV inputs without reusing REF results

#### Scenario: Change the review montage while no algorithm request is active

- **WHEN** the user changes the shared review montage from REF to AV
- **THEN** every algorithm panel's request configuration resolves to AV
- **AND** REF outputs are cleared or marked stale
- **AND** new calculations use AV inputs without reusing REF results

#### Scenario: Change montage while run or preview work is active
- **WHEN** the workspace montage changes while an old Run, dynamic-window request, or background full task is active
- **THEN** old work is cancelled or its result is rejected by revision identity
- **AND** old progress and results cannot overwrite the current selection's state
- **AND** new work uses one consistent current selection revision

#### Scenario: Change the review montage while a panel request is active

- **WHEN** an old Run, preview, or window request is active while the review montage changes
- **THEN** the old request is cancelled or rejected by selection revision
- **AND** old results cannot overwrite the current panel and new work uses one captured current snapshot

#### Scenario: Switch montage for paired or multi-channel algorithms
- **WHEN** FAA, BrainBeat, or another algorithm declares multiple input roles
- **THEN** each role is mapped through the same selected montage snapshot
- **AND** role pairing, channel order, and algorithm-specific quality rules remain intact
- **AND** a missing role produces a structured failure rather than a single-channel substitute

#### Scenario: Request a cursor window while full analysis is progressing
- **WHEN** a combined-mode full task retains a pending window and the user requests another cursor window
- **THEN** the full producer does not consume on-demand queue, project, or recording quota
- **AND** on-demand producers remain subject to capacity and shared compute concurrency limits
- **AND** an active producer for the same window is reused without duplicate computation or lease takeover
