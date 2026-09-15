# Unified Spectral Debug and Run Executor Design

## Goal

Make configured PSD, configured spectrogram, and Run-backed metric debug views
share one backend-authored provenance schema, while extracting concrete analysis
execution from `RunService` without changing mathematical results or APIs.

## Provenance

Configured spectral service responses gain additive `analysis_provenance` using
the existing `analysis-provenance-v1` shape. The spectral service creates it
from its actual response fields: requested/actual ranges, channel list,
reference, sampling rate, filter, Welch contract including explicit step,
frequency coverage, quality, and algorithm/config versions. It does not invent
a Run ID or claim a configured request is a persisted Run.

`AnalysisProvenancePanel` accepts that same shape. Spectrum and spectrogram
dialogs replace duplicated base grids with the panel. They retain only their
own backend-returned sections: PSD band power and single-window points; or
spectrogram matrix/time-bin/quality/trend details. No dialog derives Welch
step, integrates PSD, or guesses quality.

## Executor Boundary

Create `RunAnalysisExecutor` with dependencies on recording, definition, and
metric services. Its `execute(analysis_type, recording, resolved)` returns the
same `(summary, arrays, unit)` tuple currently used by `RunService`.

It owns legacy analysis, configured spectrum, configured spectrogram, static
definition metric, and dynamic definition metric execution. `RunService`
retains request resolution, cache identity, persistence, status transitions,
structured errors, and Artifact writes. Public routes, models, SQLite schema,
cache keys, artifact contents, and error status semantics remain unchanged.

## Safety Rules

- Preserve `offline-spectral-v3`, units, frequency edges, quality gates, and
  dynamic warm-up/window semantics exactly.
- Existing configured response fields remain available; provenance is additive.
- The frontend never calculates scientific evidence.
- Executor extraction is behavior-preserving and covered by existing golden
  tests plus direct facade-versus-executor equivalence tests.

## Verification

- Component tests assert PSD and spectrogram dialogs render the common panel
  and backend-provided Welch step.
- Backend tests assert configured responses include matching provenance values.
- Run tests assert summaries, artifacts, cache reuse, gate failures, and
  dynamic metric series remain unchanged after executor extraction.
- Full backend pytest, frontend Vitest, production build, and diff checks pass.

## Implementation Record

Implemented on `spectral-debug-executor`:

- Configured PSD and spectrogram responses now return additive provenance;
  their spectral contract and time semantics are unchanged.
- Both debug dialogs use the common provenance panel and retain their
  specialized backend evidence.
- `RunAnalysisExecutor` now executes every production Run path. `RunService`
  and the persistent queue delegate to it for numerical work while retaining
  lifecycle and persistence ownership. The old private facade delegates too,
  so no active path can use its historical inline implementation.

Verification: backend `197 passed`; frontend `99 passed`; production build
passed. The Vite chunk-size warning is pre-existing and non-blocking.
