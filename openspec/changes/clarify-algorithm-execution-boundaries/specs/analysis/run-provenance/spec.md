## ADDED Requirements

### Requirement: Publish one canonical current algorithm result contract

New official algorithm Runs SHALL publish one versioned result shape with explicit output kind, units, requested and actual ranges, channel roles/order, quality and failure reason, evidence, and artifact references. The current root contract SHALL be `official-algorithm-result-v1`; scalar dynamic summaries and points SHALL use `dynamic-analysis-frame-v6`. Backend and desktop clients SHALL use this current contract; duplicate legacy result fields and fallback parsing for retired result shapes SHALL NOT be emitted or consumed. This change SHALL NOT add a reader, converter, or special presentation path for retired result formats. Existing stored bytes are outside this behavior contract and are not modified by this change.

#### Scenario: Retrieve a current official algorithm result
- **WHEN** a client retrieves a Run produced under the current result contract
- **THEN** it receives the canonical result shape, units, range, quality, evidence, and immutable artifact metadata
- **AND** it does not receive duplicate fields emitted solely for a retired result format

#### Scenario: Persist a result with unavailable science
- **WHEN** an algorithm cannot produce a valid numerical value
- **THEN** the canonical result carries null output, explicit quality/failure state, and the evidence available for diagnosis
- **AND** no legacy fallback field converts the unavailable value into a number

#### Scenario: Reject an invalid completed official result
- **WHEN** a completed official result violates the current version, shape, units, ranges or explicit-state contract at a read, preview, export or provenance boundary
- **THEN** the backend returns HTTP 409 with `ALGORITHM_RESULT_INVALID` before decoding its numerical artifacts
- **AND** it does not convert the payload, rerun the algorithm or rewrite the stored result

## MODIFIED Requirements

### Requirement: Provide run resources

The API SHALL provide create, retrieve, list, cancel, and artifact-list operations under `/api/runs`. Standard creation SHALL use the persistent queue and return HTTP 202 with the stable Run identifier and current state; it SHALL NOT require a retired synchronous analysis or result route.

#### Scenario: Create a run

- **WHEN** a valid run request is posted
- **THEN** the service queues the run and returns HTTP 202 with its stable identifier and current status for polling

#### Scenario: Cancel a terminal run

- **WHEN** cancellation is requested for a completed, failed, gate-failed, or already cancelled run
- **THEN** the service returns a structured conflict and does not rewrite terminal provenance

#### Scenario: Cancel a running queued worker task

- **WHEN** cancellation is requested for a cancellable running task
- **THEN** the worker observes cancellation at an execution checkpoint and persists the cancelled terminal state
- **AND** accepting cancellation does not promise immediate interruption of an in-progress numerical operation
