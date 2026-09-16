# analysis/algorithm-runtime Specification

## Purpose
TBD - created by archiving change replace-algorithm-runtime-and-theta-beta-v2. Update Purpose after archive.
## Requirements
### Requirement: Execute every selectable algorithm through the runtime

The system SHALL resolve each selectable official or user algorithm through a
single registered runtime contract.  Generic Run lifecycle and execution
services SHALL NOT branch on a concrete algorithm ID.

#### Scenario: Run a registered algorithm

- **WHEN** a client submits a valid selected algorithm version and config
- **THEN** the runtime validates that module's configuration and raw channel
  selections, executes its static or dynamic method, and persists the returned
  result, actual ranges, quality, implementation identity, and artifacts

#### Scenario: Request an unknown or unavailable version

- **WHEN** a client submits an unknown algorithm identity or unavailable
  version
- **THEN** the system returns a structured error and creates no completed Run

### Requirement: Provide backend-authored algorithm schemas

The system SHALL provide one algorithm catalog whose entries declare source,
availability, scientific and implementation versions, supported modes, output
schema, and safe parameter schemas.  The frontend SHALL NOT derive scientific
requirements from an algorithm name, graph, quality rules, or channel order.

#### Scenario: Render a selected official algorithm

- **WHEN** a client reads the catalog and selects Theta/Beta
- **THEN** it receives a raw-channel selector, static/dynamic controls,
  analysis-range inputs, and dynamic-window inputs from the backend schema

### Requirement: Separate scientific configuration from presentation state

The system SHALL persist input channels, requested range, mode, dynamic window,
and refresh step in a Run configuration.  Display-only chart history, zoom,
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

### Requirement: Preserve historical result evidence without legacy execution

The system SHALL keep existing persisted Run summaries, provenance, and
artifacts readable and exportable after retired algorithm calculators are
removed.  It SHALL NOT allow a historical algorithm version to enter the new
runtime.

#### Scenario: Inspect a retired Run

- **WHEN** a client reads a Run created under a retired algorithm identity
- **THEN** it receives the stored result/provenance and an immutable historical
  status without attempting to import or execute a retired calculator

