# Projects and Batch Runs

## Scope

Change 06 turns the local analysis Run resource into a persistent single-worker
queue and adds a non-identifying research hierarchy. It preserves the Viewer,
configured PSD, Spectrogram, and legacy analysis endpoints. This is a local
single-user workstation feature, not multi-tenant research data management.

## Local Research Hierarchy

`Project` contains study metadata. `Subject` has only a project-scoped internal
`local_code`; the API rejects unexpected fields, so it does not accept names,
birthdates, addresses, or other participant identity data. `Condition` is an
optional project-scoped experimental label. `Session` connects a Project,
Subject, existing immutable Recording, and optional Condition.

The BDF/EDF source remains in the global recording store. A Session references
its stable recording ID and never moves, copies, or overwrites source data.
Project text and local codes are not added to artifact filenames or scientific
configuration digests.

## Persistent Run Queue

`POST /api/runs` now returns HTTP `202 Accepted` with a persisted `queued` Run.
The client polls `GET /api/runs/{run_id}`. A single daemon worker uses a short
SQLite transaction to claim exactly one queued Run, then runs the existing
backend analysis implementation. No EEG mathematics is moved to the frontend.

Each queued Run has the existing source/configuration/environment provenance
plus optional project, batch, parent, and idempotency identities. Repeating a
non-empty idempotency key returns the original Run. On application startup,
stranded `running` work is marked interrupted and requeued with its existing
Run ID before work resumes.

Cancelling a queued Run is immediate. A running calculation is cooperative:
the request is recorded, then the worker suppresses publication of its result
and marks the Run `cancelled` before writing an artifact. A calculation already
inside a numerical library call cannot be safely preempted by SQLite; this is a
deliberate limitation, not a claim of instant cancellation.

`POST /api/runs/{run_id}/retry` creates a new queued Run linked by
`parent_run_id`; it never mutates the historical Run.

## Batch Runs

`POST /api/batch-runs` freezes Project identity, analysis type, optional
definition identity/version, and request configuration. It accepts only
Recording IDs already associated with that Project through Sessions. Each
eligible Recording receives a deterministic child idempotency key.

Batch item outcomes remain explicit:

- `completed`
- `gate_failed`
- `missing_channel`
- `insufficient_duration`
- `failed`
- `cancelled`

Missing channel and short duration are preflight states with no generated
scientific output. They are never represented as a zero-valued result. Batch
cancellation and retry delegate to the child Run lifecycle.

## Boundaries

The queue has one local worker by design. There is no browser execution,
distributed scheduler, PII storage, arbitrary Python, multi-user permission
model, or clinical conclusion. Outputs and exports remain Change 07.
