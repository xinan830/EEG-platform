## Why

RBP already computes absolute band power per channel, but only exposes relative shares as structured outputs, so the existing topomap cannot display absolute power without reading diagnostic evidence. Expose both result types through the current RBP calculation and let the topomap select between them without rerunning the algorithm.

## What Changes

- Add per-channel absolute power arrays for all five RBP bands, in `uV^2`, to static, dynamic, and on-demand structured outputs while preserving existing relative-power arrays and formulas.
- Add an explicit relative/absolute power selector to the existing RBP topomap API and WPF panel; keep it independent from the frequency-band selector.
- Add an optional band selector to topomap requests. Omission returns all five bands; selecting one band returns and interpolates only that band.
- Record request-correlated backend durations for RBP spectrum loading and band integration, and for topomap interpolation and response serialization without logging EEG samples or result arrays.
- Make old results without the declared arrays return a structured unavailable result for absolute-power maps; do not reconstruct values from diagnostic evidence.
- Advance the RBP Definition and implementation identity for the expanded result contract while retaining its scientific version.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `analysis/official-rbp-faa-brainbeat-execution`: declare per-channel absolute power outputs and their units as part of the current RBP result contract.
- `analysis/eeg-topomap`: allow RBP maps to select absolute or relative band power with explicit values and units.

## Impact

- Backend RBP frame, static/dynamic/on-demand output declarations, Definition metadata, topomap adapter, API model, route and stage diagnostics.
- WPF topomap request client, RBP Review ViewModel and panel controls.
- Backend and desktop contract tests. No new dependency, algorithm catalog entry, database migration, or change to the RBP formula, preprocessing, channel order, montage, or time window.
- Existing persisted runs remain readable. They retain their relative-power maps; absolute-power maps are unavailable unless their structured result declares the absolute arrays.
