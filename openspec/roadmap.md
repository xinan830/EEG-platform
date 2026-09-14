# EEG research platform OpenSpec roadmap

This roadmap evolves the existing EEG viewer into a local, single-user research workstation without breaking the current viewer and analysis interfaces. Only one change may be active at a time.

## Delivery order

| Order | Change | Outcome | Status |
| --- | --- | --- | --- |
| 01 | `complete-traceable-analysis-foundation` | Reproducible recordings, runs, artifacts, cache identities, and validation reports | Complete, archived 2026-09-14 |
| 02 | `standardize-research-primitives` | Typed scientific values, units, quality propagation, and reusable nodes | Complete, archived 2026-09-14 |
| 03 | `add-algorithm-definition-engine` | Immutable versioned definitions and a validated DAG executor | Complete, archived 2026-09-14 |
| 04 | `migrate-official-algorithms` | Shadow parity and controlled migration of official metrics | Complete, archived 2026-09-14 |
| 05 | `add-algorithm-form-builder` | Form and JSON authoring, validation, preview, versioning, and comparison | Complete, archived 2026-09-14 |
| 06 | `add-projects-and-batch-runs` | Research hierarchy, persistent jobs, and batch execution | Complete, archived 2026-09-14 |
| 07 | `add-results-validation-and-export` | Result workbench, independent validation, and reproducible exports | Complete, archived 2026-09-14 |
| 07a | `add-independent-spectral-reference-validation` | Persisted SciPy/MNE pointwise PSD reference evidence | Complete, archived 2026-09-14 |
| 08 | `define-extension-and-governance-boundaries` | Safe module metadata and explicit future security boundaries | Complete, archived 2026-09-14 |
| 09 | `surface-spectral-reference-validation` | Workbench UI for independent PSD reference evidence | Complete, archived 2026-09-14 |
| 10 | `localize-official-algorithm-labels` | Chinese official algorithm display labels | Complete, archived 2026-09-14 |
| 11 | `add-user-friendly-algorithm-workbench` | Chinese-readable default algorithm explanations with opt-in developer details | Complete, archived 2026-09-14 |
| 12 | `improve-core-research-workbench-ux` | Task-oriented navigation and ordinary/developer presentation separation | Complete, archived 2026-09-14 |

## Change gate

For every change:

```powershell
openspec validate <change-id> --strict --no-interactive
# Implement every task and produce the validation report.
openspec archive <change-id>
openspec validate --all --strict --no-interactive
```

A change is complete only when its task list, tests, documentation, validation report, archive, and post-archive strict validation all pass. The next change starts only after that point.

## Invariants across all changes

- Existing `/spectrum/configured`, `/spectrogram/configured`, and legacy analysis APIs remain available.
- Source EEG files are immutable and are never overwritten by derived data.
- Internal EEG values remain `float64` volts; public values have explicit units and convert once.
- Viewer montage/filter behavior never becomes the implicit Analysis reference/filter behavior.
- Failed or rejected scientific output is `null` with a reason, never a fabricated zero.
- Definition versions and implementation build identities are distinct provenance fields.
- No real EEG, personal identity data, credentials, database files, or generated build output enter Git.

## Deferred scope

Multi-tenancy, organization permissions, an algorithm marketplace, arbitrary Python execution, signed third-party plugins, Parquet export, and PDF reporting require separate future changes. They are not silently included in changes 01-08.
