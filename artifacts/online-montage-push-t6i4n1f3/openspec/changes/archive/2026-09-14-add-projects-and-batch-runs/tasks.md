## 1. Project hierarchy

- [x] 1.1 Add additive migrations and typed Project, Subject, Condition, and Session models/repositories.
- [x] 1.2 Add structured Project API CRUD/listing and project-scoped recording/session listing without PII fields.
- [x] 1.3 Test migration idempotency, project isolation, local-code uniqueness, and recording association.

## 2. Persistent Run queue

- [x] 2.1 Extend Run provenance with project/batch context, idempotency, interruption recovery, and cancellation state.
- [x] 2.2 Refactor RunService into enqueue plus worker execution while retaining an internal synchronous compatibility facade.
- [x] 2.3 Add single-worker transactional claim, restart recovery, retry, cancel, and queue API behavior returning 202.
- [x] 2.4 Test idempotency, recovery, cancellation, retry, no duplicate artifacts, and legacy analysis compatibility.

## 3. Batch runs

- [x] 3.1 Add BatchRun persistence, frozen request/definition identity, and deterministic child-run idempotency.
- [x] 3.2 Add create/query/cancel/retry BatchRun API and structured per-recording outcomes.
- [x] 3.3 Test completed, gate_failed, missing_channel, insufficient_duration, failed, cancelled, and project-membership cases.

## 4. Documentation and archive

- [x] 4.1 Document local hierarchy, queue lifecycle, cancellation/recovery limits, privacy boundary, and API contract.
- [x] 4.2 Run backend regression tests, frontend checks/build, strict change validation, and migration regression tests.
- [x] 4.3 Archive the change, update roadmap, run strict all-spec validation, commit, and synchronize GitHub.
