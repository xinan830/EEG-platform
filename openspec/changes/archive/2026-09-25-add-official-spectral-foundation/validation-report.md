# Official Spectral Foundation Validation Report

## Scope

- PSD Runtime module: `psd`, scientific version `welch-v1`.
- STFT Runtime module: `stft`, scientific version `spectrogram-v2`, implementation
  identity `stft-runtime-v1`.
- Test recording ranges: static `[0, 30] s`, sampling rate `100 Hz`, selected
  channel `Fz`.
- STFT output: window-center time axis in `s`, frequency axis in `Hz`, linear
  power in `V^2/Hz`, display power in `dB re 1 uV^2/Hz`.
- Quality evidence preserves input gaps separately from transform padding;
  rejected windows are represented as unavailable (`NaN`) values by the frozen
  spectrogram contract.
- Structured arrays are persisted as NPZ artifacts; Run summaries contain only
  array keys, units, shapes, axis metadata, quality, and provenance.

## Evidence

- Backend suite: `279 passed`.
- Independent Hann FFT/STFT reference: `tests/test_official_algorithm_independent_references.py`.
- PSD/STFT Run and artifact integration: `tests/test_official_algorithm_runs.py`.
- Artifact round-trip and corruption detection: `tests/test_run_foundation.py`.
- OpenSpec strict validation: passed.
- `git diff --check`: passed.

## Known boundaries

- PSD and STFT currently expose static Runtime modes only. Static and
  box-selection paths are covered; dynamic execution is explicitly rejected
  until a declared matrix-series STFT/PSD contract exists.
- The file-size policy still reports two pre-existing reviewed-size findings:
  `backend/app/services/recordings.py` and `frontend/src/App.vue`.

## Rollback point

The repository state immediately before this STFT integration is commit
`a99403a0fe27a9bcf80ba5acefcd038569f01237`.
