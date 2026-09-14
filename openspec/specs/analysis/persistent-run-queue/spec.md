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
