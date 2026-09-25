## Why

The official PSD and STFT Runtime modules currently support static analysis
only. The existing scalar `AlgorithmSeriesResult` cannot represent a sequence
of frequency-series or time-frequency matrices without losing axes, units, or
per-window quality evidence. Dynamic spectral analysis therefore needs its own
typed contract before either module can be enabled dynamically.

## What Changes

- Add a typed dynamic spectral-series contract with recording-relative sample
  windows, explicit window states, shared axes, matrix shapes, units, and
  artifact references.
- Define separate dynamic output semantics for PSD (`window x frequency`) and
  STFT (`window x time-within-window x frequency`).
- Reuse the existing scientific gateways and quality policy; do not introduce
  a second FFT, Welch, or spectrogram implementation.
- Execute each planned window from a validated half-open sample range and
  preserve gaps as unavailable windows rather than zero-filled EEG.
- Persist matrices and per-window evidence as immutable artifacts while keeping
  only bounded metadata in Run summaries and API responses.
- Add independent reference, state-transition, artifact round-trip, and static
  versus one-window equivalence tests.

## Impact

The change affects backend Runtime contracts, official PSD/STFT modules, Run
serialization, provenance, and backend API result metadata. Static PSD/STFT
results remain byte-compatible at the scientific contract level. No frontend
scientific computation or raw recording format changes are included.

## Non-Goals

- Changing the frozen `offline-spectral-v3` or `spectrogram-v2` mathematics.
- Making dynamic output a scalar trend or reusing `AlgorithmSeriesResult` for
  matrices.
- Adding dynamic UI controls in this change.
- Replacing device-trigger synchronization or recording lifecycle timing.

## Risks And Rollback

The main risk is a mismatch between window coordinates and persisted matrix
rows. Every row therefore carries its half-open sample range and state, and a
single-window dynamic run must be tested against the existing static result.
The change can be rolled back at the Runtime/module boundary; static modules
and existing artifacts remain readable.
