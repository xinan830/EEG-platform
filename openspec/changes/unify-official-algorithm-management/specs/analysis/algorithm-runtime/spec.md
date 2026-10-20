## MODIFIED Requirements

## ADDED Requirements

### Requirement: Centrally manage dynamic execution strategies

The platform SHALL centrally manage the lifecycle and orchestration semantics
for full dynamic analysis, on-demand dynamic analysis, and combined on-demand
preview plus background full analysis. Each algorithm SHALL explicitly declare
which strategies it supports and each reuse direction independently: full
result to on-demand preview, and on-demand result to full Run. Shared orchestration SHALL own scheduling,
cancellation, status, window identity, persistence, and reuse decisions;
algorithm modules SHALL retain ownership of scientific calculation and
algorithm-specific output/evidence.

#### Scenario: Run an on-demand dynamic window

- **WHEN** a client requests a dynamic analysis value at a cursor location using on-demand strategy
- **THEN** the shared runtime resolves the canonical window identity and schedules exactly that window through the owning algorithm module
- **AND** the result reports its actual sample/time bounds, state, quality, and provenance

#### Scenario: Run combined preview and full analysis

- **WHEN** a client requests combined strategy for an algorithm that declares support
- **THEN** the system schedules an on-demand preview for the requested cursor and background full analysis for the selected range
- **AND** it reports preview and background progress/status independently
- **AND** it reuses a window only when source, active scientific identity, config, preprocessing, and canonical sample bounds match
- **AND** support for reuse in one direction does not imply support in the other direction

#### Scenario: Algorithm does not support a strategy

- **WHEN** a client requests a strategy that the algorithm has not declared as supported
- **THEN** the system returns a structured unsupported-capability reason
- **AND** it does not silently substitute another strategy

#### Scenario: Full analysis encounters an on-demand window

- **WHEN** full analysis reaches a completed or in-flight on-demand window
- **THEN** the shared orchestrator reuses or joins it only if the declared reuse policy and exact window identity permit it
- **AND** otherwise full analysis computes its own window without changing the scientific contract

#### Scenario: Reuse direction is not declared

- **WHEN** an algorithm supports full and on-demand execution but has no declaration for one reuse direction
- **THEN** the shared runtime does not reuse results in that direction
- **AND** each strategy follows its own validated computation path

### Requirement: Execute every selectable algorithm through the runtime

The system SHALL resolve every selectable official algorithm to its explicitly
declared current runtime module and scientific version before queueing. Generic
Run lifecycle and execution services SHALL NOT branch on a concrete algorithm
ID. The resolved current scientific version SHALL be persisted and passed
unchanged to execution. Retired scientific versions SHALL NOT be selectable or
executable through compatibility adapters.

#### Scenario: Run a registered algorithm

- **WHEN** a client submits a valid selected algorithm and config
- **THEN** the runtime validates that module's configuration and raw channel
  selections, executes its static or dynamic method, and persists the returned
  result, actual ranges, quality, implementation identity, and artifacts

#### Scenario: Request an unknown or unavailable version

- **WHEN** a client submits an unknown algorithm identity or retired/unavailable version
- **THEN** the system returns a structured error and creates no completed Run

#### Scenario: Queue a resolved official algorithm

- **WHEN** a client submits an official algorithm Run without a client-supplied
  Definition identity
- **THEN** the backend resolves and persists the platform Definition ID,
  published current Definition version, algorithm scientific version and module digest
- **AND THEN** the queue executes that exact current scientific version

#### Scenario: Explicit current version resolution

- **WHEN** an official algorithm is registered for new Runs
- **THEN** exactly one current scientific version is explicitly declared for its algorithm ID
- **AND** resolution does not choose a version by lexical or numeric latest-version sorting

#### Scenario: Multiple installed scientific versions

- **WHEN** more than one scientific version is present in stored or deployment metadata for an algorithm ID
- **THEN** only the explicitly active current version is exposed for new execution
- **AND** a retired version is rejected rather than selected by lexical sorting or compatibility fallback

#### Scenario: Retired Run is not executable

- **WHEN** a Run references a retired scientific version
- **THEN** the runtime does not load a compatibility calculator or execute that Run
- **AND** it returns a structured retired-version/unavailable status

### Requirement: Provide backend-authored algorithm schemas

The system SHALL provide one algorithm catalog whose entries declare source,
availability, scientific and implementation versions, supported modes, output
schema, and safe parameter schemas. The catalog SHALL expose the explicitly
current version only for each official algorithm ID. The frontend SHALL NOT
derive scientific requirements from an algorithm name, graph, quality rules,
or channel order.

#### Scenario: Render a selected official algorithm

- **WHEN** a client reads the catalog and selects Theta/Beta
- **THEN** it receives a raw-channel selector, static/dynamic controls,
  analysis-range inputs, and dynamic-window inputs from the backend schema

#### Scenario: Do not expose retired official versions

- **WHEN** a client requests available official algorithms
- **THEN** it receives only the explicitly current version for each algorithm ID
- **AND** no retired version is offered as a selectable choice

### Requirement: Separate scientific configuration from presentation state

The system SHALL persist input channels, requested range, mode, dynamic window,
and refresh step in a Run configuration. Display-only chart history, zoom,
and panel state SHALL remain client-local and SHALL NOT affect numerical output
or cache identity.

#### Scenario: Change chart history only

- **WHEN** a user changes a dynamic trend's displayed history range
- **THEN** no new scientific Run is submitted and the prior numerical output
  remains unchanged

### Requirement: Produce dynamic results from one frame contract

The system SHALL plan every dynamic algorithm point through one backend-owned,
endpoint-aligned `DynamicAnalysisFrame` contract. Each returned point SHALL
state its actual range, warm-up state, result-contract version, quality, and
available spectral provenance. Algorithm modules SHALL NOT independently
invent time-window scheduling or load a configured duration that differs from
the point's actual duration.

#### Scenario: Return an early warm-up point

- **WHEN** playback reaches 8 s with a configured 10 s dynamic analysis
  window
- **THEN** the system returns one warm-up point for exactly `0–8 s`, anchors it
  at `8 s`, and loads only that real EEG range

#### Scenario: Reuse a dynamic result cache

- **WHEN** a persisted dynamic-result evidence contract has changed
- **THEN** the system does not reuse a cached result produced under the old
  contract version

### Requirement: Target only the current official algorithm version

The current application SHALL target only the explicitly current official
algorithm versions. It SHALL NOT provide migration, execution, interpretation,
or display compatibility for Runs and artifacts produced by retired official
algorithm versions. Algorithm-management updates SHALL NOT intentionally delete
stored recordings, Runs, or artifacts; their accessibility is not guaranteed.

#### Scenario: Retired-version data has no compatibility guarantee

- **WHEN** a Run or artifact was produced by a retired scientific version
- **THEN** the current application is not required to migrate, execute, interpret, or display it
- **AND** any stored bytes may remain without being accessible through current product workflows

### Requirement: Module-owned execution evidence

Each executable module SHALL supply a serializable execution snapshot for its
window, preprocessing and quality contract. The runtime SHALL validate common
provenance evidence and module extension evidence before persisting a completed
Run.

#### Scenario: Persist FAA execution snapshot

- **WHEN** FAA completes a Run
- **THEN** the Run stores its paired-epoch processing and quality snapshot from
  the FAA module
- **AND THEN** the generic Run service contains no FAA-specific branch
