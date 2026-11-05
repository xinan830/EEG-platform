## Purpose

Defines one montage selection for each active desktop workspace and ensures every signal consumer in that workspace observes the same selected montage snapshot. Processing pipelines may remain separate while sharing selection identity.

## ADDED Requirements

### Requirement: One authoritative montage selection per workspace

Each active recording-review workspace SHALL have exactly one writable montage selection associated with its recording and immutable montage snapshot. Waveform display and every algorithm panel in that workspace SHALL consume this selection; consumers SHALL NOT expose or retain an independently writable montage choice.

#### Scenario: Change montage in recording review
- **WHEN** the user selects another available montage in the workspace montage control
- **THEN** the waveform display and every algorithm panel observe that same montage selection
- **AND** no consumer continues using a separately selected montage as its current input

#### Scenario: Open two recordings in separate workspaces
- **WHEN** two recording-review workspaces are active for different recordings
- **THEN** each workspace has its own selected montage state
- **AND** changing one workspace does not change the other workspace's selection

### Requirement: Consumers preserve pipeline ownership while sharing selection

Waveform display and algorithm execution SHALL remain independent processing pipelines with their own lifecycle, caches, and processing state. Both pipelines SHALL resolve their inputs from the workspace's same selected montage identity and snapshot; algorithm-specific input roles and channel subsets MAY differ but SHALL be mapped through that snapshot.

#### Scenario: Algorithm requests semantic input roles
- **WHEN** an algorithm requires one or more channel roles
- **THEN** the algorithm resolves those roles against the workspace-selected montage
- **AND** the request records the exact selected montage identity and resolved channel order

#### Scenario: Selected montage cannot satisfy an algorithm
- **WHEN** the selected montage or recording lacks a required algorithm input role
- **THEN** that algorithm reports a structured unavailable or validation state
- **AND** it does not use another montage, positional channel, or prior result as a substitute

### Requirement: Montage changes invalidate work from prior selections

Changing the workspace montage SHALL advance the selection revision. Waveform and algorithm consumers SHALL invalidate or mark stale any cached or in-flight output whose selection revision does not match the current revision. Historical completed results MAY remain inspectable but SHALL NOT be presented as results for the current selection.

#### Scenario: Change montage while requests are in flight
- **WHEN** the selected montage changes before waveform or algorithm work completes
- **THEN** results from the prior selection cannot overwrite or appear as current output
- **AND** new work uses the current montage snapshot and revision

#### Scenario: Change montage with cached waveform data
- **WHEN** the user selects a montage for which display data was previously cached
- **THEN** cache reuse occurs only when recording identity, time/sample range, montage snapshot identity, and processing parameters all match

#### Scenario: Keep the last complete waveform while a montage changes
- **WHEN** the selected montage changes and its replacement waveform frame is still being prepared
- **THEN** the workspace keeps the last complete waveform frame visible
- **AND** it atomically replaces that frame when a complete frame for the selected montage is ready
- **AND** the chart is not cleared solely because the montage changed

### Requirement: Workspace selection is released with its owner

The workspace SHALL release montage selection subscriptions and dependent resources when that workspace closes or switches recordings. A new recording workspace SHALL initialize from that recording's own available immutable montage catalogue and documented default rule.

#### Scenario: Close and reopen a recording workspace
- **WHEN** a workspace is closed and the same recording is opened again
- **THEN** the old workspace instance and its subscriptions are released
- **AND** the new instance initializes from the recording's available montage snapshots without inheriting another recording's selection
