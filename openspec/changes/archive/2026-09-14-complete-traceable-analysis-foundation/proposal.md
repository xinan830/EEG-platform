## Why

Current analysis results cannot be reproduced reliably because the platform does not persist source-file hashes, complete execution parameters, build/runtime identity, quality reasons, or large-result artifact identities. This foundation is required before algorithms become configurable or batch-executable; otherwise later features would multiply untraceable results and unstable cache behavior.

## What Changes

- Add non-destructive, versioned SQLite migrations that upgrade existing databases without deleting recordings, analyses, events, audits, or report snapshots.
- Enrich recordings with SHA-256, byte size, raw and canonical channel metadata, channel type, unit, and import-contract version.
- Add persistent `AnalysisRun` records with lifecycle, reproducibility snapshots, structured errors, requested/actual ranges, execution environment, and deterministic cache identity.
- Store large numerical outputs as compressed NPZ artifacts on the filesystem; persist only indexes, scalars, summaries, paths, units, and artifact SHA-256 values in SQLite/JSON.
- Add `ValidationRun` persistence and exportable JSON engineering-validation reports.
- Expand spectral quality reasons to include `non_finite`, `amplitude_threshold`, `flatline`, `clipping`, and `missing_samples`; rejected numerical outputs remain null/non-values rather than fabricated zeros.
- Add compatible Run and Validation APIs while leaving all existing recording, spectrum, spectrogram, legacy analysis, event, audit, and report APIs in place.

No public API is removed. The offline spectral reference, filters, Welch configuration, band boundaries, units, and golden values are not changed by this change.

## Capabilities

### New Capabilities

- `analysis/run-provenance`: Persistent analysis lifecycle, deterministic cache identity, execution snapshots, immutable artifacts, cancellation, and structured failure behavior.
- `validation/validation-runs`: Persistent engineering-validation records and reproducible JSON report export.

### Modified Capabilities

- `data/recordings`: Record source-byte identity, import version, and explicit raw/canonical channel metadata while preserving existing recordings.
- `analysis/spectral`: Expand quality reasons and make rejected outputs explicitly unavailable without changing the `offline-spectral-v3` mathematical contract.

## Impact

- Backend: new migration, provenance, artifact, run, validation, API model, service, and route modules; Recording and spectral quality integration changes.
- Storage: SQLite gains versioned schema and additional tables/columns; compressed NPZ files live under a derived-artifact directory separate from source EEG files.
- APIs: new `/api/runs`, `/api/runs/{id}`, `/api/runs/{id}/cancel`, `/api/runs/{id}/artifacts`, and `/api/validations` resources; legacy endpoints remain compatible.
- Dependencies: no new runtime package is required because SQLite, hashlib, platform metadata, JSON, and NumPy NPZ support already exist.
- Migration: startup applies ordered transactions once, records `schema_version`, backfills hashes/metadata when source files exist, and preserves readable legacy rows when a source is unavailable.
- Rollback: a failed migration transaction leaves the previous schema version intact; filesystem artifacts are written atomically before their metadata rows are committed.
- Non-goals: asynchronous worker queues, algorithm-definition DAGs, projects, batch runs, frontend authoring UI, multi-tenancy, clinical validation, and arbitrary Python execution.
