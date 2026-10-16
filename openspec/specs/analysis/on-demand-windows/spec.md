# analysis/on-demand-windows Specification

## Purpose
Define traceable, bounded, algorithm-neutral execution and retrieval of individual dynamic analysis windows without changing existing full AnalysisRun semantics.
## Requirements
### Requirement: Ensure a backend-planned dynamic window

The system SHALL provide an opt-in window capability only for algorithms that declare a validated window executor. The client SHALL identify a recording, algorithm definition/version, complete scientific configuration, and recording-relative cursor sample. The backend SHALL resolve the scientific window plan and stable window identity. Existing full Run endpoints and algorithms without the capability SHALL retain their current behavior.

#### Scenario: Request a supported PSD window

- **WHEN** an authorized client ensures a PSD window for a valid recording and supported configuration/cursor
- **THEN** the backend returns a stable session/window identity, backend-planned half-open sample range, current lifecycle state, request identity, and a way to observe that work
- **AND** the client cannot override the authoritative scientific window boundaries

#### Scenario: Algorithm has no window capability

- **WHEN** a client requests an algorithm that has not declared the validated capability
- **THEN** the backend returns a structured unsupported-capability response
- **AND** it does not silently run a full AnalysisRun or fabricate an empty window

### Requirement: Preserve scientific identity and provenance per window

Every published window SHALL be immutable and keyed by source recording identity/hash, project/authorization scope, resolved definition identity, scientific and implementation identities, normalized complete scientific configuration, window-policy version, and actual half-open sample range. Display-only state SHALL NOT affect the key. A result SHALL report source/config/version provenance, actual sample/time range, axes/shape/units, quality/evidence, preprocessing semantics, and integrity status.

#### Scenario: Reuse an exact window

- **WHEN** the same authorized configuration ensures a previously published, integrity-verified window
- **THEN** the backend returns the same scientific window identity and values
- **AND** a different source hash, channel order/reference, scientific config, implementation identity, or window-policy version cannot reuse it

#### Scenario: No future window is available at the cursor

- **WHEN** the cursor has not reached the first policy-eligible window or required real samples are unavailable
- **THEN** the service reports insufficient-data or a structured source-gap state
- **AND** it does not choose a future window, join samples across a gap, or emit numeric zero

### Requirement: Publish window artifacts atomically

The system SHALL make a result readable only after its bounded payload, schema, units, shape, provenance, and checksum have been validated and atomically committed. Existing completed Run rows and artifacts SHALL NOT be appended to, overwritten, or reinterpreted as window sessions. Interrupted temporary writes SHALL remain unreadable and recoverable or explicitly fail.

#### Scenario: Worker crashes during publication

- **WHEN** a worker stops before atomic publication completes
- **THEN** clients cannot read the partial scientific result
- **AND** recovery either safely retries the uncommitted attempt or returns a structured failure

### Requirement: Bound scheduling and share work safely

The system SHALL deduplicate in-flight work by exact window identity, enforce finite queue/concurrency/resource bounds, and prioritize accepted foreground targets over optional prefetch. Work and client subscriptions SHALL have distinct lifecycles. One subscriber cancelling SHALL NOT cancel work still required by another authorized subscriber. Retries SHALL use a new attempt/request identity and SHALL NOT be permanently pinned to a cancelled result.

#### Scenario: Concurrent ensure for the same window

- **WHEN** two authorized consumers request the same not-yet-ready window concurrently
- **THEN** they observe one shared computation and independently addressable subscriptions
- **AND** cancelling either subscription leaves the other consumer's work/result intact

#### Scenario: Resource capacity is exhausted

- **WHEN** the bounded work queue or resource budget is full
- **THEN** the backend returns a structured queued/rejected-capacity response according to policy
- **AND** it does not create an unbounded task backlog or silently increase worker concurrency

#### Scenario: Preparation or persistent storage budget is exhausted

- **WHEN** admitting new work would exceed the persistent-result budget, or continuous source preparation cannot allocate within its temporary-file and free-disk budgets
- **THEN** admission returns `WINDOW_CAPACITY_EXCEEDED`, or the accepted attempt terminates as `failed` with `WINDOW_RESOURCE_EXCEEDED` and quantitative resource details
- **AND** the backend does not truncate the recording, change preprocessing, or delete published scientific results
- **AND** existing ready reads and subscriptions to already admitted work remain available

#### Scenario: Explicitly retry a cancelled or failed window

- **WHEN** an authorized consumer ensures the same source/configuration/range with `retry=true`, `retry_of_window_id` naming a cancelled or failed window, and a new `request_id`
- **THEN** the repository atomically creates its next execution generation with a new window ID, attempt ID and subscription
- **AND** repeated or concurrent retries naming the same original window resolve to the same successor generation
- **AND** the original window remains terminal and readable; ready, scientific rejection, insufficient-data and unavailable results are not reopened

#### Scenario: Recover a crashed worker

- **WHEN** an uncommitted attempt's lease expires
- **THEN** the repository fences the old token, retains the expired attempt, and requeues the nonterminal window with a new attempt identity
- **AND** a worker with the expired token cannot publish a result or change the window's state
- **AND** recovery validates source/configuration identity before submitting the replacement computation

### Requirement: Preserve scientific output and distinguish lifecycle states

The service SHALL distinguish source preparation, queued work, window computation, ready scientific output, insufficient data, rejected/unavailable science, cancellation, and execution failure. Missing scientific values SHALL be null with structured reasons. A ready window SHALL match the same window from the existing full dynamic Run under the declared algorithm-specific tolerance and SHALL retain the existing preprocessing contract.

#### Scenario: PSD window equivalence

- **WHEN** a PSD window and its matching full dynamic Run window use identical source bytes, configuration, identity, and scientific contract
- **THEN** their sample bounds, frequency axes, values, units, quality/evidence, and preprocessing provenance match within the declared tolerance
- **AND** the implementation does not locally filter only the requested short window to avoid continuous zero-phase preparation

#### Scenario: RBP window equivalence

- **WHEN** an opted-in RBP window and its full dynamic Run use identical source bytes, acquisition/analysis reference, channel, definition, configuration and window policy
- **THEN** the five named Delta/Theta/Alpha/Beta/Gamma ratios, actual sample range, quality and full window evidence match within `rtol=1e-12, atol=1e-12`
- **AND** both paths use RBP's existing continuous 1–50 Hz preprocessing and 50 Hz notch before slicing, without borrowing PSD's configurable filter range
- **AND** unavailable denominators, gaps and scientific quality rejection retain the existing RBP failure contract without zero shares

#### Scenario: Per-algorithm opt-in stays disabled until final acceptance

- **WHEN** additional algorithms pass their independent automated scientific and module validation during the user-approved migration phase
- **THEN** they may declare their window capability behind independent default-off backend and WPF flags
- **AND** GUI playback, parameters, failure visibility and rollback remain explicit final acceptance work, not implied by successful automated tests

#### Scenario: Scalar spectral window equivalence

- **WHEN** Band Ratio, Peak Frequency, IAPF or Theta/Beta executes an opted-in single window with identical source and scientific configuration to a full dynamic Run
- **THEN** its value, actual range, quality, failure and evidence match the corresponding full Run point within `rtol=1e-12, atol=1e-12`
- **AND** both paths retain continuous preprocessing before slicing and use the configured window length to identify Partial versus Complete
- **AND** a missing value is never emitted as zero; Theta/Beta preserves its historical `dimensionless` unit

#### Scenario: Scientific rejection differs from execution failure

- **WHEN** the requested samples fail a scientific quality rule
- **THEN** the service publishes/returns the algorithm's rejected or unavailable state and structured quantitative evidence
- **AND** it does not report the quality outcome as a fabricated success or generic infrastructure failure

### Requirement: Keep legacy Run behavior compatible

Adding on-demand windows SHALL NOT alter existing full Run creation, ensure/reuse, progress, cancellation, retry, completed artifacts, export, structured preview, or result-window read behavior. Historical Run results SHALL remain readable without migration.

#### Scenario: Existing full dynamic Run remains available

- **WHEN** an existing client creates or reads a full dynamic Run while on-demand capability is enabled
- **THEN** its status, result/artifact contract, and API behavior remain unchanged
- **AND** opting out of on-demand execution continues to use the existing full Run path

### Requirement: Expose a stable window HTTP contract

The backend SHALL expose the opt-in window resource through these routes:

- `POST /api/analysis-windows/ensure` accepts `recording_id`, `algorithm_id`,
  optional definition identity, complete scientific `config`, recording-relative
  integer `cursor_sample`, optional `project_id`, and an optional client
  `request_id`. Explicit retry additionally requires `retry=true`,
  `retry_of_window_id` and a new `request_id`; retry generation is backend-owned.
  The client SHALL NOT submit authoritative `start_sample`,
  `end_sample`, or scientific output arrays.
- When `request_id` is present, the ensure response SHALL create one scoped
  subscription for that consumer and return `subscription_id`; cancelling that
  subscription SHALL be safe when the window is already terminal.
- `GET /api/analysis-windows/{window_id}` returns the current state, backend
  planned sample/time range, request identity, and structured failure when one
  exists. It SHALL return scientific arrays only for an integrity-verified
  ready window.
- `POST /api/analysis-windows/{window_id}/subscriptions` creates a scoped
  subscription and returns its identity; `DELETE
  /api/analysis-windows/subscriptions/{subscription_id}` cancels only that
  subscriber.

The ensure route SHALL return `202` for newly queued or in-flight work and
`200` when an existing ready window is returned. The API SHALL use these stable
error codes independent of localized messages: `RECORDING_NOT_FOUND` (404),
`ON_DEMAND_CAPABILITY_UNSUPPORTED` (409), `WINDOW_CURSOR_INVALID` (422),
`WINDOW_IDENTITY_CONFLICT` (409), `WINDOW_PROJECT_FORBIDDEN` (403),
`WINDOW_CAPACITY_EXCEEDED` (429), `WINDOW_NOT_FOUND` (404), and
`WINDOW_RESULT_CORRUPT` (409). Every error SHALL include the platform
`request_id`.

#### Scenario: Client sends scientific window boundaries

- **WHEN** an ensure request contains `start_sample`, `end_sample`, or output arrays
- **THEN** validation fails with `WINDOW_CURSOR_INVALID`
- **AND** no session, window, subscription, or attempt is created

#### Scenario: A ready result is requested

- **WHEN** a client gets a ready window whose artifact checksum and schema have passed validation
- **THEN** the response includes the immutable result payload, provenance, quality, and integrity metadata
- **AND** the response status is `200`

#### Scenario: One subscriber cancels

- **WHEN** a subscriber deletes its subscription while another authorized subscriber remains active
- **THEN** the API returns the cancelled subscription
- **AND** the shared window attempt remains unaffected

