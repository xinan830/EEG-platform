# desktop/workspace-montage-selection Specification

## Purpose

为每个活动回放工作区提供唯一的导联选择，并确保该工作区中的波形和算法消费者读取同一不可变导联快照。各自的处理流程仍独立运行。
## Requirements
### Requirement: One authoritative montage selection per workspace

Each active recording-review or standalone algorithm workspace SHALL create exactly one writable montage selection associated with its recording and validated immutable montage catalogue. Waveform display and review algorithm panels SHALL consume the review instance; standalone algorithm previews and Runs SHALL consume the algorithm-workspace instance. Consumers SHALL NOT retain an independent current montage value.

#### Scenario: Open a standalone algorithm workspace

- **WHEN** an algorithm workspace registers a completed recording with a valid montage catalogue
- **THEN** it creates one recording-bound `WorkspaceMontageSelection` instance
- **AND** the instance defaults to the documented acquisition montage
- **AND** all algorithm views in that workspace read its selected snapshot

#### Scenario: Change montage in recording review

- **WHEN** the user selects another available montage in a recording-review workspace
- **THEN** the waveform display and every review algorithm panel observe that same montage selection
- **AND** no consumer continues using a separately selected montage as its current input

#### Scenario: Open two recordings in separate workspaces

- **WHEN** two recording-review or standalone algorithm workspaces are active for different recordings
- **THEN** each workspace has its own selected montage state
- **AND** changing one workspace does not change the other workspace's selection

#### Scenario: Isolate two workspace instances

- **WHEN** recording review and standalone algorithm workspaces are active, or two algorithm workspaces use different recordings
- **THEN** each workspace owns its own selection instance
- **AND** changing one instance does not change the other

#### Scenario: Change the algorithm workspace montage

- **WHEN** the user selects another validated montage in the standalone algorithm workspace
- **THEN** the selection instance advances its revision
- **AND** all subsequent preview and Run requests use the new snapshot
- **AND** no consumer keeps a separately writable montage choice

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

### Requirement: Release workspace selection with its owner

The standalone algorithm workspace SHALL unsubscribe and release its selection instance when the registered recording changes or the workspace is disposed. A replacement instance SHALL initialize only from the replacement recording's catalogue.

#### Scenario: Replace the registered algorithm recording

- **WHEN** the algorithm catalog releases one registered recording and registers another
- **THEN** the old selection subscription is detached
- **AND** the new recording receives a new selection instance
- **AND** the old selection cannot update the new recording's requests

#### Scenario: Close and reopen a recording workspace

- **WHEN** a workspace is closed and the same recording is opened again
- **THEN** the old workspace instance and its subscriptions are released
- **AND** the new instance initializes from the recording's available montage snapshots without inheriting another workspace's selection
