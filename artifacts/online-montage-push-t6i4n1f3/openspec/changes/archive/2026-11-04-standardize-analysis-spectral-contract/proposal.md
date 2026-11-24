## Why

Algorithm calculations currently inherit several spectral preprocessing defaults from separate algorithm and service paths, while waveform review has its own mutable display filter. This makes the same recording vulnerable to inconsistent analysis inputs and makes result provenance harder to compare.

## What Changes

- Establish a backend-owned spectral execution contract independent of review waveform display filters.
- Make the shared spectral coverage baseline 1–50 Hz while allowing each algorithm to select its scientifically defined frequency range, time window, Welch segmentation, quality policy, and bands.
- Default algorithm analysis to a 50 Hz notch and expose an explicit user-selectable off value; record the effective setting in run identity and provenance.
- Introduce `offline-spectral-v4` for the changed shared preprocessing behavior so historical v3 results remain identifiable and are never silently reused under the new contract.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `analysis/spectral`: update the shared frequency coverage and preprocessing contract while keeping algorithm-owned estimator settings and bands explicit.
- `analysis/official-algorithm-execution`: separate algorithm preprocessing from display settings and make effective spectral settings traceable per run.
- `desktop/algorithm-detail-presentation`: expose the algorithm-owned 50 Hz notch default and off option without binding it to the review waveform filter controls.

## Impact

Affected systems include backend scientific contracts, spectral preprocessing and cache identity, official algorithm configuration schemas and snapshots, PSD-derived algorithms, WPF algorithm parameter serialization and presentation, tests, and current spectral documentation. The request/response contract changes for applicable official runs; the new preprocessing identity must not reuse results computed under earlier scientific contracts. Raw recordings and review display filtering remain unchanged.
