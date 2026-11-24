# Dynamic spectral series tasks

## 1. Contract

- [x] Define `AlgorithmStructuredSeriesResult` with typed axes, units, matrix
  shape, window rows, state counts, and bounded evidence.
- [x] Validate half-open sample windows and reject invalid sampling/range
  transitions before loading EEG data.
- [x] Preserve `Partial`, `Complete`, `Rejected`, and `Unavailable` as the
  canonical per-window states; keep legacy `warmup` only for old scalar Runs.
- [x] Add explicit gap versus transform-padding evidence to every window row.

## 2. Runtime and storage

- [x] Add shared dynamic matrix execution to `AlgorithmRuntime` without moving
  scientific formulas into the executor.
- [x] Persist matrix series and full window evidence as immutable NPZ artifacts
  with checksum and bounded Run summary metadata.
- [x] Expose artifact-backed arrays through the existing result API without
  frontend recomputation.
- [x] Preserve static PSD/STFT result contracts and historical artifact reads.

## 3. Official modules

- [x] Enable dynamic PSD using one frozen Welch calculation per planned window.
- [x] Enable dynamic STFT using one frozen spectrogram calculation per planned
  window with normalized inner time centers.
- [x] Add parameter schemas and manifest policies for supported windows, steps,
  minimum samples, and quality gates.
- [x] Reject invalid dynamic configurations with a typed Runtime error.

## 4. Verification

- [x] Add independent PSD and STFT matrix-series references with explicit
  tolerances and unit checks.
- [x] Prove one-window dynamic/static equivalence for PSD and STFT.
- [x] Test gap, rejected, partial, unavailable, and transform-padding states.
- [x] Test artifact round-trip, checksum failure, API metadata, and provenance.
- [x] Run backend tests, OpenSpec strict validation, file-size policy, and diff
  checks; record a validation report and rollback commit.
