## Context

The local application already has immutable source recordings, AnalysisRuns,
artifact hashes, and SQLite migrations. It needs a project hierarchy and a
queue without moving scientific computation into the browser or replacing the
offline spectral contract.

## Design

### Project hierarchy

`projects`, `subjects`, `conditions`, and `sessions` are additive SQLite
tables. A session associates one project, subject, recording, and optional
condition. The original `recordings` table stays globally addressable and its
source file is never moved or overwritten.

Subject input is `local_code`, unique inside a project. API payloads reject
name, birthday, address, and free-form identity fields by using explicit
Pydantic models. Project display text is not written to source-file paths,
configuration hashes, or artifact filenames.

### Persistent queue

An AnalysisRun remains the unit of execution. Enqueue persists it as `queued`;
one `RunWorker` claims work transactionally and transitions it to `running`.
Execution continues to call the existing backend domain implementation. A
restart transitions stranded `running` work through `interrupted` back to
`queued` before the worker claims it.

The client may supply an idempotency key. The repository returns the existing
run for the same key rather than inserting a second run. Batch children use a
deterministic batch/recording key. Queue cancellation is immediate for queued
runs. A running scientific calculation is cooperative: cancellation is marked
and the worker discards its completed result as `cancelled` before publication.

`RunService.create()` remains a synchronous internal compatibility facade for
tests and local code, but HTTP `POST /api/runs` uses `enqueue()` and returns
`202`.

### Batch runs

A BatchRun stores an immutable request snapshot, definition identity, and
project identity. It creates child queued AnalysisRuns in recording order. The
batch summary is computed from child state and reports `completed`,
`gate_failed`, `missing_channel`, `insufficient_duration`, `failed`, and
`cancelled` separately. Missing channel and insufficient duration are
structured preflight errors, not fabricated numerical results.

## Risks and Controls

- SQLite only permits one effective writer: one worker and short transactional
  claims avoid concurrent queue consumers.
- Cancellation cannot safely interrupt a C-extension calculation midway: it is
  cooperative and explicit; no partial scientific artifact is published.
- A definition version can be frozen in metadata even if an execution adapter
  does not exist. The request is rejected before enqueue rather than silently
  running a different algorithm.
- Project membership is checked for batch recordings; no inferred subject or
  channel mapping is allowed.
