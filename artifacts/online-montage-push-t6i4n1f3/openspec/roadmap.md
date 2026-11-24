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
| 13 | `add-research-metric-formula-builder` | Chinese user-facing feature selection and safe formula composition | Complete, archived 2026-09-14 |
| 14 | `improve-algorithm-library-readability` | Readable saved formulas and provenance-safe private deletion | Complete, archived 2026-09-14 |
| 15 | `clean-platform-architecture-debt` | Remove only confirmed obsolete compatibility paths, decouple feature navigation from MainWindow, keep composition ownership explicit, and contain generated build output | Deferred until active EEG topomap changes are complete |
| 16 | `add-rbp-absolute-power-topomap` | Expose RBP per-channel absolute band power and add an absolute/relative power selector to the existing 2D topomap | Complete, archived 2026-11-03 |
| 17 | `stabilize-rbp-topomap-color-scales` | Keep relative and absolute RBP topomap color meanings stable during playback | Active |
| 18 | `add-3d-eeg-topomap` | Add validated MNE-to-GNM coordinates, backend surface fields, and a reusable RBP 3D presentation | Queued; standard mapping file pending from user |

## Next Planned Direction

`stabilize-rbp-topomap-color-scales` is active. The 3D
`add-3d-eeg-topomap` change remains queued until the user supplies its required
standard mapping file. The existing candidate mapping is explicitly an
approximate demo in MNE `head` coordinates with non-SI GNM units, while the
platform's standard resolver returns MNE `mri` coordinates in meters; it must
not be used as the production mapping.

The active color-scale change fixes relative power at `0–1` and calibrates the
absolute scale independently for each frequency band at the first ready window
of an RBP analysis session. Later windows keep those endpoints; a new session
calibrates again. This affects display mapping only and does not alter results.

After this change, resume the queued 3D work only after validating the supplied
mapping and target mesh identities, frames, units, transform, site coverage,
scalp region, and distribution license. Do not use the existing approximate
demo mapping as production input.

`clean-platform-architecture-debt` remains queued for after this change. Its
planned slices are:

1. **Remove confirmed obsolete compatibility paths.** Inventory each path and
   classify it as required historical-file support, a current scientific
   implementation, an active adapter, or an obsolete fallback. Remove only
   confirmed obsolete internal fallbacks after caller, stored-data, API, and
   provenance checks. Migrate transitional WPF type namespaces only after their
   `x:Class`, generated BAML, and composition references move together. Keep
   historical EEG file support. ANT-derived Crimson and Zenlite TNK children
   are current scientific calculations, not compatibility code merely because
   their source names contain `legacy`.
2. **Reduce composition-root responsibilities.** Map construction, lifecycle,
   and navigation responsibilities currently held by `App` and `MainWindow`.
   Keep one composition root and one owner for shared state; move feature-owned
   construction detail behind explicit module composition only where it reduces
   coupling. Do not add a service locator or a dependency-injection framework.
3. **Tighten navigation boundaries.** Move feature navigation requests out of
   Views' direct `MainWindow` casts to the narrowest command/navigation
   boundary that fits each workflow. Keep view event handling in code-behind
   only where it is presentation-specific. Preserve `Window.GetWindow` when it
   is used solely to assign modal-dialog ownership.
4. **Contain generated build output.** Route isolated build/test output under
   the existing ignored temporary root. Add ignore rules only for verified
   generated paths. Remove existing isolated outputs only after confirming no
   build, test, or desktop process is using them; never clean recordings,
   logs, or other user data as part of this work.

Each slice requires focused dependency/build tests, OpenSpec strict validation,
`git diff --check`, and a recorded manual check where user-visible navigation or
WPF resource loading changes. Preserve recording formats, persisted results,
algorithm identities, units, time semantics, and scientific behavior.

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
