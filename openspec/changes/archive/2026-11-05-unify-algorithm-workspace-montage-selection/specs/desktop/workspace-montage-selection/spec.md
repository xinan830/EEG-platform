## MODIFIED Requirements

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
