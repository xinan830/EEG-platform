## Why

TNK dynamic playback currently calls the shared offline spectral loader, whose cache miss reads and zero-phase filters the complete recording before slicing one requested spectrum. This makes the first result scale with recording duration, even though each TNK spectral child requests only a 4-second or 30-second range.

## What Changes

- Give TNK dynamic spectral inputs a dedicated sample-bounded preparation path that reads only the child's declared sample range plus a finite filter context.
- Keep the existing `offline-spectral-v3` full-recording contract unchanged for other consumers.
- Version and record the TNK window-preprocessing boundary policy in scientific identity, results, and cache provenance.
- Measure local-window results against the existing full-recording path before enabling the new contract; explicitly document numerical differences and quality behavior.
- Preserve sample-major float64 V values, role/channel order, half-open sample coordinates, gap rejection, quality gates, and checkpoint state semantics.

## Capabilities

### New Capabilities
- `analysis/tnk-window-spectral-source`: Bounded, versioned spectral source preparation for TNK dynamic windows.

### Modified Capabilities
- `analysis/on-demand-windows`: Permit TNK windows to use their explicitly versioned window-scoped preprocessing contract rather than implying parity with the former whole-recording preprocessing semantics.

## Impact

- Backend: TNK group source requests, the identity-bound recording access session, spectral window preparation, TNK scientific/cache identity, provenance, and focused validation coverage.
- The WPF/API request shape remains unchanged. Other algorithms and ordinary offline PSD/STFT consumers retain `offline-spectral-v3` behavior.
- Compatibility: existing stored TNK results remain readable under their recorded scientific identity; new TNK dynamic results use a new scientific/cache identity and do not reuse old windows.
- Scientific-contract impact: filtering a finite window with finite context can differ near its boundaries from filtering the full recording. The new path must report its scope and edge policy, and must not be enabled until the measured difference and acceptance threshold are recorded.
- Migration: deploy the bounded path as a new TNK scientific version after focused numerical and gap-boundary validation. No source recording or historical result migration is required.
- Non-goal: removing full-recording preprocessing from generic offline algorithms, changing TNK formulas/window cadence/units/channel mapping, or changing waveform display filtering.
