# Algorithm runtime reset audit

Date: 2026-09-16

This audit is the deletion gate for `replace-algorithm-runtime-and-theta-beta-v2`.
No source path is deleted only because its filename looks old; each candidate is
classified by its imports and callers first.

## Intended canonical ownership

| Responsibility | Canonical owner after cutover |
| --- | --- |
| Signal loading, preprocessing, PSD, quality and units | `backend/app/scientific` contracts/primitives; `eeg_core` is compatibility/reference-only |
| IAPF science | `backend/app/algorithms/iapf` |
| Single-channel Theta/Beta v2 science | `backend/app/algorithms/theta_beta` |
| User Definition graph execution | retired boundary under `backend/app/legacy`; historical reads only |
| Registry, schemas, windows, normalized results | `backend/app/algorithm_runtime` |
| Run lifecycle, cache, provenance and artifacts | `backend/app/services` |
| Display-only chart state | Vue components/local state |

## Current deletion status

Compatibility adapters remain intentionally. They are still required by
historical imports, validation/reference tests, and old read paths. The
deletion task is therefore not complete until the safety rules below are
machine-checked against the current caller inventory; this is an explicit
deferred migration, not an accidental omission.

The public Run API already rejects new `definition_metric` creation with the
stable `USER_DEFINED_ALGORITHM_RETIRED` response. The persistent queue retains
the old executor only for compatibility coverage until its internal execution
callers are migrated; it is not an active official-algorithm path.

## Candidate classification

| Candidate | Initial classification | Deletion/migration gate |
| --- | --- | --- |
| `eeg_core/official_algorithms/iapf.py` | current mathematics | move behind canonical IAPF module, then remove duplicate entry only after equivalence tests |
| `eeg_core/official_algorithms/theta_beta.py` | legacy bundled metric implementation | replace with single-channel v2; retain only shared band-integration primitive if independently used |
| `eeg_core/official_algorithms/registry.py` | removed compatibility entry point | callers use `app.algorithms.catalog` |
| `eeg_core/official_definitions.py` | removed compatibility entry point | callers use `app.algorithms.catalog` |
| `eeg_core/official_algorithm_shadows.py` | removed validation entry point | callers use `app.eeg_core.official_algorithms.validation` |
| `eeg_core/offline_metrics.py` | compatibility wrapper and historical caller | delete only after old runtime callers are moved and historical reads use stored artifacts |
| `processing/offline_analysis.py` | current viewer/analysis path until runtime cutover | preserve viewer behavior; split reusable primitives before deleting executable algorithm branches |
| `models/official_algorithm_run.py` | current mapped official request model | replace with runtime config identity after new Run contract tests pass |
| `models/recording.py::ChannelMapping` | global semantic mapping | remove active storage/API/UI after historical snapshot migration tests pass |
| `api/recordings.py` mapping route | active mapping API | retire with structured endpoint error after no current caller remains |
| `services/runs.py` official branches | generic service coupling | remove concrete-ID branches and delegate to `AlgorithmRuntime` |
| `services/run_analysis_executor.py` official branches | generic executor coupling | remove concrete-ID branches and normalize module results |

## Known active callers to migrate

- `api/runs.py` currently catches `OfficialChannelMappingRequired`.
- `services/runs.py` resolves `official_algorithm` and reads recording mapping.
- `services/run_analysis_executor.py` formats the normalized Runtime result;
  legacy formatting callers remain outside the active official path.
- `api/recordings.py`, `services/recordings.py`, `models/recording.py`, and
  `App.vue` expose the global mapping flow.
- Independent validation helpers still import the old `offline_metrics` facade;
  these references remain until the legacy comparison implementation is retired.
- `AlgorithmDisplayWorkspace.vue` has fixed official IDs, units, and request
  shapes.

## Deletion safety rules

1. A candidate is not deleted until `rg` shows no current import/call outside
   historical readers and test fixtures.
2. Historical Run retrieval must deserialize stored JSON/NPZ without importing
   deleted calculators.
3. Every moved formula has a before/after golden test with explicit tolerance.
4. Mapping storage is removed only after an idempotent migration test proves
   raw channel labels and historical snapshots remain readable.
