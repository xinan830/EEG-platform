# analysis/offline-analysis-orchestration Specification

## ADDED Requirements

### Requirement: Preserve two scientific execution modes

The offline analysis platform SHALL keep `full` and `on_demand` as the only scientific execution modes. A combined user strategy SHALL orchestrate one on-demand session and one full Run without changing algorithm formulas, preprocessing semantics, window definitions, units, quality rules, or provenance meaning.

#### Scenario: Combined strategy uses one scientific identity

- **WHEN** a user selects “on-demand preview plus background full” for a dynamic algorithm
- **THEN** the preview request and the background Run use the same recording identity, montage/channel mapping, reference, filters, parameters, window policy, requested range, scientific version, and implementation identity
- **AND** the combined label is not persisted as a third algorithm execution mode.

#### Scenario: Unsupported algorithm falls back safely

- **WHEN** the selected algorithm does not declare validated on-demand capability
- **THEN** the combined strategy is unavailable
- **AND** the client uses the existing full Run path without creating an on-demand session.

### Requirement: Prioritize the current preview window

The backend SHALL prioritize the latest valid on-demand target over background full windows while bounding CPU, memory, disk, and active task usage.

#### Scenario: Cursor moves while full analysis is running

- **WHEN** a new on-demand target is submitted while a full Run is computing another dynamic window
- **THEN** queued obsolete preview targets may be discarded
- **AND** the latest target is admitted ahead of background work
- **AND** the full Run yields at a safe window boundary when interactive demand is pending.

#### Scenario: Background work receives execution time

- **WHEN** no interactive target is pending
- **THEN** the scheduler allows the background Run to make progress
- **AND** repeated interaction cannot permanently starve the background Run.

### Requirement: Keep cancellation ownership independent

The system SHALL keep preview subscription cancellation and background Run cancellation independent.

#### Scenario: User closes the detail page

- **WHEN** the WPF detail page is closed while the combined strategy is active
- **THEN** only the page-owned on-demand subscription and polling are released
- **AND** the background Run remains persistent and visible to the task/status surface.

#### Scenario: User cancels background completion

- **WHEN** the user explicitly cancels the background full Run
- **THEN** the full Run transitions through the existing cancellation contract
- **AND** the current on-demand preview remains usable.

### Requirement: Publish only identity-matched results

Every preview or full result published to the current detail page SHALL match the current scientific identity and exact half-open sample window.

#### Scenario: Old response arrives after a new target

- **WHEN** a response for an older cursor generation arrives after a newer target has been requested
- **THEN** the older response is retained only as a cacheable historical result
- **AND** it cannot replace the current displayed window.

#### Scenario: Cache artifact validation fails

- **WHEN** a candidate cached window has a missing, corrupt, or mismatched manifest, axis, shape, unit, quality, evidence, or scientific cache key
- **THEN** the candidate is rejected with a structured cache-miss reason
- **AND** the window is recomputed or remains explicitly unavailable.

### Requirement: Preserve complete-result semantics

The system SHALL expose full Run progress separately from preview readiness and SHALL allow complete export/statistics only after the full Run reaches its terminal completed state with a valid complete artifact.

#### Scenario: Full Run is partially complete

- **WHEN** only part of the requested range has completed
- **THEN** the UI reports completed-window count and total-window count separately from the ready preview window
- **AND** it does not label the partial result as a complete analysis.

### Requirement: Keep algorithm-specific scientific boundaries

Cross-mode cache reuse SHALL be enabled only for algorithms that declare and pass independent numerical validation for the relevant window semantics and preprocessing scope.

#### Scenario: Algorithm has global preprocessing dependency

- **WHEN** an algorithm requires a global fit, long-range state, or a distinct continuous preprocessing contract
- **THEN** the scheduler may share orchestration state only
- **AND** it SHALL NOT reuse another algorithm's prepared source or claim cross-mode numerical equivalence without a dedicated contract and evidence.

### Requirement: Separate scientific, plan, window, and request identities

The offline analysis service SHALL define separate identities for scientific configuration, analysis plan, exact window, and UI request generation. Only the first three may participate in scientific cache identity; request generation SHALL exist solely to reject stale UI responses.

#### Scenario: Same window under different analysis plans

- **WHEN** two requests address the same sample interval but have different requested ranges, anchors, preparation scopes, window policies, algorithm versions, or scientific configuration
- **THEN** they receive different `AnalysisPlanKey` or `ScientificConfigKey` values
- **AND** the service does not reuse the result merely because the visible interval is equal.

#### Scenario: Stale UI generation arrives

- **WHEN** a result carries an older `RequestGeneration` than the current subscriber
- **THEN** the result may remain available as a historical cache entry
- **AND** it SHALL NOT be published to the current UI subscriber.

### Requirement: Require a verifiable prepared-source contract

An algorithm that supports on-demand execution or cross-mode reuse SHALL publish a `PreparedSourceContract` describing its input/preparation range, effective output range, filter and reference state, gap/boundary behavior, unit, preparation status, implementation identity, manifest, and validated numerical tolerance.

#### Scenario: Prepared source is not equivalent

- **WHEN** a candidate prepared source has a different scope, boundary state, filter/reference semantics, unit, implementation identity, or manifest than the requested contract
- **THEN** the service rejects reuse with a structured preparation miss reason
- **AND** it waits for a compatible preparation or executes a separately validated path.

### Requirement: Coordinate shared windows with durable single-flight

The scheduler SHALL coordinate full and on-demand consumers through one shared `WindowKey` ownership point. Multiple consumers of an eligible window SHALL share one scientific computation and publication, while each consumer retains an independent subscription and cancellation state.

#### Scenario: Preview and full request the same window

- **WHEN** an on-demand target and a background full Run request the same eligible `WindowKey`
- **THEN** the scheduler creates at most one active computation for that key
- **AND** the interactive consumer may raise priority without creating a duplicate calculation.

#### Scenario: One consumer unsubscribes

- **WHEN** the preview subscriber is released while the full Run still owns the shared window
- **THEN** the computation remains eligible to complete for the full Run
- **AND** the preview release does not cancel or invalidate the full consumer's result.

#### Scenario: Interactive demand is continuous

- **WHEN** interactive requests remain pending for an extended period
- **THEN** the scheduler continues to grant the background Run its configured minimum execution share
- **AND** records waiting, yielding, execution, and resource-rejection measurements.

### Requirement: Gate full completion on complete coverage and artifact validity

The service SHALL mark a full Run `completed` only when every planned window has a persisted terminal state, quality/reason metadata, valid sample information, and the complete artifact range, index, manifest, count, and checksum have passed validation.

#### Scenario: Full Run has unresolved windows

- **WHEN** at least one planned window remains queued, running, unknown, or lacks persisted terminal metadata
- **THEN** the Run remains non-terminal or failed according to the existing contract
- **AND** it SHALL NOT be reported as a complete analysis.

#### Scenario: Coverage contains rejected or unavailable windows

- **WHEN** a planned window reaches a legal `Rejected` or `Unavailable` terminal state
- **THEN** it contributes to coverage accounting with its structured reason
- **AND** it is excluded from numeric statistics without being silently removed.

### Requirement: Make full Run creation idempotent

The backend SHALL derive a stable idempotency identity from the scientific configuration, analysis plan, requested range, and execution intent so duplicate UI clicks, HTTP retries, page reconstruction, and recovery do not create duplicate active Runs. An explicit user retry SHALL create a new attempt identity while retaining the prior Run.

#### Scenario: Duplicate submission is retried

- **WHEN** the same scientific request is submitted more than once without an explicit retry intent
- **THEN** the backend returns or reconnects to the existing active Run
- **AND** it does not create another active Run for the same identity.
