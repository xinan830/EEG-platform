## Why

The ANT-Amplify EEG process project contains a related set of biofeedback calculations that users want available in brain-platform. The platform already has its own same-named algorithms and must keep them unchanged, so the ANT-derived calculations need distinct TNK identities and the existing official-algorithm governance, execution strategies, provenance, and result contracts.

## What Changes

- Add TNK-prefixed official modules for the scientific calculations implemented by ANT-Amplify, keeping each formula and parameter contract owned by its module.
- Replace the individual TNK catalog entries with one user-facing “TNK 脑指标” group entry. The group owns one canonical Fz/Pz/Oz/F3/F4 role-to-source mapping snapshot and dispatches that mapping to independently implemented TNK modules.
- Keep TNK modules independently testable and scientifically owned behind the group coordinator; the group is an execution composition boundary, not a new formula.
- Preserve the existing platform algorithm identities, current scientific definitions, and results; do not overwrite them or add compatibility behavior.
- Define deterministic offline semantics for calculations that depend on rolling windows, IAPF locking, warm-up, smoothing, or cross-window history across full, on-demand, and combined strategies.
- Expose TNK algorithms through the current backend catalog and WPF algorithm workspace using module-owned parameters and result projections.
- Expose the public `tnk_biofeedback` group only through dynamic analysis. New static TNK Run requests are rejected with a structured unsupported-mode error; existing persisted static Run records remain readable and are neither migrated nor re-executed.
- Recreate the ANT offline workbench chart layout with SciChart chart-family tabs, a reusable filtered review-style waveform, playback and backend-owned score-state duration presentation. The user's subsequent scope correction excludes event annotation and event-period report controls. Single windows use time points; on-demand windows accumulate bounded real-time histories instead of categorical columns.
- Add independent reference comparisons against the ANT source behavior and synthetic-signal fixtures; record any deliberate differences explicitly.
- Add the three ANT-Amplify raw-Fz paths to the same internal TNK group: Crimson neural-network meditation, Zenlite calmness/awareness, and Zenlite spectral relaxation. They use a typed raw time-series source (V/float64), module-owned 250/256 Hz resampling and fixed causal preprocessing; they are not substituted with the platform PSD path.

## Capabilities

### New Capabilities
- `analysis/tnk-biofeedback-algorithms`: TNK-prefixed offline algorithms and their scientific result contracts.

### Modified Capabilities
- `analysis/algorithm-runtime`: Deterministic context replay and explicit strategy support for history-dependent offline algorithms.
- `desktop/wpf-official-algorithms`: Catalog-driven discovery, configuration, and result display for the new TNK modules.

## Impact

- Backend: new self-contained modules under `backend/app/algorithms/tnk_*`, closed-registry composition, current scientific manifests, evidence, and independent tests.
- Desktop: catalog-driven algorithm detail modules and result handlers; all calculations remain backend-owned.
- Scientific contracts: new stable TNK identities. Existing platform algorithm IDs and current definitions remain unchanged. ANT-derived definitions are frozen separately and engineering parity does not claim clinical validity.
- Data/API: use the existing Analysis Run and artifact contracts unless a reviewed output requirement proves a typed contract extension is necessary. Any API or persistence change must state its consumers and failure behavior.
- Dependencies: reuse the platform's pinned scientific stack where it can reproduce the declared reference; any dependency addition or version change requires a separate justification and frozen reference identity.
- Non-goals: porting PyQt views, ANT/eego/LSL device acquisition, BDF recording, experiment controls, or the real-time streaming process. This change adds offline scientific Runs, not a live biofeedback runtime.
- Compatibility and migration: no legacy calculator, alias, result conversion, or fallback is added. New TNK identities are current-only; existing platform algorithms and stored data are not rewritten or deleted.
