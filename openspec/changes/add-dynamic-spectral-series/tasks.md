# Dynamic spectral series tasks

## 1. Contract

- [ ] Define `AlgorithmStructuredSeriesResult` with typed axes, units, matrix
  shape, window rows, state counts, and bounded evidence.
- [ ] Validate half-open sample windows and reject invalid sampling/range
  transitions before loading EEG data.
- [ ] Preserve `Partial`, `Complete`, `Rejected`, and `Unavailable` as the
  canonical per-window states; keep legacy `warmup` only for old scalar Runs.
- [ ] Add explicit gap versus transform-padding evidence to every window row.

## 2. Runtime and storage

- [ ] Add shared dynamic matrix execution to `AlgorithmRuntime` without moving
  scientific formulas into the executor.
- [ ] Persist matrix series and full window evidence as immutable NPZ artifacts
  with checksum and bounded Run summary metadata.
- [ ] Expose artifact-backed arrays through the existing result API without
  frontend recomputation.
- [ ] Preserve static PSD/STFT result contracts and historical artifact reads.

## 3. Official modules

- [ ] Enable dynamic PSD using one frozen Welch calculation per planned window.
- [ ] Enable dynamic STFT using one frozen spectrogram calculation per planned
  window with normalized inner time centers.
- [ ] Add parameter schemas and manifest policies for supported windows, steps,
  minimum samples, and quality gates.
- [ ] Reject unsupported dynamic configurations with a typed Runtime error.

## 4. Verification

- [ ] Add independent PSD and STFT matrix-series references with explicit
  tolerances and unit checks.
- [ ] Prove one-window dynamic/static equivalence for PSD and STFT.
- [ ] Test gap, rejected, partial, unavailable, and transform-padding states.
- [ ] Test artifact round-trip, checksum failure, API metadata, and provenance.
- [ ] Run backend tests, OpenSpec strict validation, file-size policy, and diff
  checks; record a validation report and rollback commit.
