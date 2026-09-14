## Why

Current EEG calculations expose arrays and dictionaries without a shared type, unit, window, quality, or provenance interface. A constrained primitive layer is required before a generic algorithm-definition executor can reject invalid graphs and reproduce valid ones.

## What Changes

- Add typed scientific values for EEG signals, windows, PSD, band power, relative power, scalars, time series, channel maps, and quality masks.
- Add explicit units and dimensional compatibility; prohibit implicit conversion.
- Add a registry of safe backend nodes for channel selection, reference, filtering, resampling, detrending, windowing, Welch, bands, arithmetic/statistics, quality gates, and output.
- Define anti-alias resampling, exact window alignment/time-center/residual policy, ordered channels, quality propagation, and provenance for every node result.
- Prove the primitives can construct fixed-band RBP and FAA without changing current official algorithm outputs or public APIs.

No frontend scientific calculation, arbitrary `eval`, DAG persistence, algorithm publishing, or legacy algorithm cutover is included.

## Capabilities

### New Capabilities

- `analysis/research-primitives`: Scientific value types, units, node contracts, safe reusable operations, quality propagation, and provenance.

### Modified Capabilities

None.

## Impact

- Adds backend-only domain modules and tests under `eeg_core/primitives`.
- Reuses NumPy/SciPy already in the runtime; no new dependency or database migration.
- Existing PSD, Spectrogram, Viewer, Run, and legacy analysis interfaces remain unchanged.
- Scientific contract impact is additive: primitives initially run in tests and later shadow execution, not as official output producers.
