## 1. Migration and recording identity

- [x] 1.1 Add centralized, ordered SQLite migrations with schema-version, idempotency, busy-timeout, and rollback tests for empty and legacy databases.
- [x] 1.2 Add recording source digest/size and aligned raw label, canonical label, channel type, unit, and import-version persistence without changing existing response fields.
- [x] 1.3 Backfill legacy recording identity when source files exist and preserve readable metadata when they do not.

## 2. Provenance and artifact infrastructure

- [x] 2.1 Add canonical JSON hashing, implementation/environment capture, configuration digest, and complete cache-identity utilities with invalidation tests.
- [x] 2.2 Add typed AnalysisRun, artifact, and structured-error models plus persistent repositories and lifecycle transition validation.
- [x] 2.3 Add a confined atomic NPZ artifact store with SHA-256 verification, unit/shape metadata, corruption tests, and no source-file mutation.

## 3. Analysis and validation services

- [x] 3.1 Add a compatible synchronous run service that records queued/running/terminal states and full execution provenance while reusing the current backend analysis implementation.
- [x] 3.2 Add run list/get/cancel/artifact endpoints with stable error codes and legacy analysis API regression coverage.
- [x] 3.3 Add persistent ValidationRun creation/list/get and versioned JSON engineering-report export with tolerance and evidence tests.
- [x] 3.4 Expand spectral quality evaluation for non-finite, amplitude, flatline, clipping, and missing samples, preserving clean-data golden values and null semantics.

## 4. Documentation and integration

- [x] 4.1 Register migrations and new routers in application startup and verify existing recording, spectrum, spectrogram, playback, event, audit, report, and analysis interfaces remain operational.
- [x] 4.2 Document schema versions, run provenance, cache identity, artifact layout, quality reasons, APIs, migration/rollback behavior, and engineering-only validation scope.
- [x] 4.3 Review changed business-file sizes and update `docs/code-size-policy.json` for any file above the 400-line soft limit.

## 5. Verification and archive readiness

- [x] 5.1 Pass strict OpenSpec validation, full backend pytest, frontend Vitest, TypeScript checking, production build, and `git diff --check`.
- [x] 5.2 Generate and persist the Change 01 validation report with commands, versions, results, evidence, residual risks, and no personal EEG data.
