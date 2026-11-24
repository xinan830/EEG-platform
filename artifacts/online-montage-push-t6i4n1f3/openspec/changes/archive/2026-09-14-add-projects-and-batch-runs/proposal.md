## Why

The platform can persist a reproducible analysis run but has no local research
container for subjects, sessions, conditions, or grouped analysis. `POST
/api/runs` also executes synchronously, so a process restart cannot recover
work and a caller cannot observe a real queue.

## What Changes

- Add local-only `Project`, `Subject`, `Session`, and `Condition` metadata.
  Subjects use an internal code only; names and identifying information are not
  accepted by these resources.
- Convert the new Run API to a SQLite-backed, single-worker queue. `POST
  /api/runs` returns `202`, while existing Viewer and configured spectral APIs
  remain unchanged.
- Add idempotency keys, retry, cancellation semantics, restart recovery, and
  structured batch outcome summaries.
- Add `BatchRun` that freezes one analysis request/definition version and
  expands it into traceable child AnalysisRuns for project recordings.

## Compatibility

No `/api/recordings/*`, Viewer, PSD, Spectrogram, or legacy analysis endpoint
is removed. The Run resource introduced in Change 01 becomes asynchronous as
specified by the research-workstation roadmap; its URL and response model stay
available, but callers must poll its returned run after receiving `202`.

## Non-Goals

This change does not add multi-user access control, participant PII, browser
worker execution, arbitrary Python, clinical interpretation, batch UI, or a
parallel worker pool. Result visualization and exports remain Change 07.
