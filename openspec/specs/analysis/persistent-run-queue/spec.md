# analysis/persistent-run-queue Specification

## Purpose
TBD - created by archiving change add-projects-and-batch-runs. Update Purpose after archive.
## Requirements
### Requirement: Enqueue a traceable AnalysisRun

The system SHALL persist a new AnalysisRun as `queued` and return it from
`POST /api/runs` with HTTP 202. It SHALL retain the input identity, configuration
digest, implementation identity, and optional project/batch context before a
worker begins scientific computation.

#### Scenario: Enqueue a spectrum run
- **WHEN** a valid run request is posted
- **THEN** the API returns HTTP 202 and a Run with `queued` or `running` status
  that can be queried by stable run ID

### Requirement: Preserve queue identity and recover interruption

The system SHALL return an existing Run for the same non-empty idempotency key.
On startup it SHALL transition stranded `running` work through `interrupted`
and requeue it before it is eligible for the single worker.

#### Scenario: Repeat an idempotent request
- **WHEN** the caller posts a second request with the same idempotency key
- **THEN** the API returns the original Run identity and creates no second job

#### Scenario: Recover after process interruption
- **WHEN** the process starts with a persisted `running` Run
- **THEN** the Run is marked interrupted and requeued without creating a new ID

### Requirement: Cancel and retry explicitly

The system SHALL cancel queued work without execution and SHALL expose retry as
a new queued Run with a new ID and a parent provenance reference. An unavailable
or cancelled result SHALL remain `null`, not a zero-valued scientific result.

#### Scenario: Cancel queued work
- **WHEN** cancellation is requested before worker claim
- **THEN** the Run becomes `cancelled` and no artifact is created

### Requirement: Report measured analysis progress
The system SHALL persist processed and total dynamic windows, including rejected
windows. It SHALL retain the last progress on cancellation/failure and report
full progress on cache completion. Playback time SHALL NOT be execution progress.

#### Scenario: Cancel a dynamic Run
- **WHEN** cancellation is observed between windows
- **THEN** the Run ends cancelled with its last processed count and no result artifact

### Requirement: Merge identical active configurations atomically
The system SHALL merge identical active scientific configuration and execution
identity within the same recording/project/batch scope atomically. Explicit keys
SHALL reject changed scientific inputs. Terminal work SHALL NOT block a new
unkeyed analysis attempt.

#### Scenario: Concurrent identical submissions
- **WHEN** two clients submit the same unkeyed configuration concurrently
- **THEN** both receive one active Run identity

### Requirement: Bound execution concurrency and isolate batch work
The system SHALL run at most two execution workers with bounded native numerical
threads and distinct interactive/batch lanes.
Static interactive Runs SHALL use the interactive lane. Dynamic and project
batch Runs SHALL use the batch lane. Two dynamic Runs MAY remain serialized.

#### Scenario: Interactive work during a batch
- **WHEN** a batch occupies its lane and an interactive Run is queued
- **THEN** the interactive lane can complete that Run independently

### Requirement: Bound raw recording and preprocessing memory
The system SHALL read long recordings by sample blocks and use owned temporary
memory maps for continuous preprocessing. Chunk boundaries SHALL NOT restart
filter state or alter scientific sample coordinates.

#### Scenario: A recording contains a gap
- **WHEN** blocks on either side of the gap are read
- **THEN** unavailable samples remain non-finite and are not silently interpolated
