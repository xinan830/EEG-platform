# Configured spectrogram migration

## Changes

- Migrated `SpectrogramPanel` from the legacy GET endpoint to `POST /spectrogram/configured`.
- Added a typed frontend client for configured spectrogram requests with backend error details preserved.
- Added traceability fields to the spectrogram response type and displayed the actual interval and algorithm version.
- Added `sfreq_hz` to the spectrogram payload so sample-index boundary tolerance is computed correctly.
- Aligned configured spectrogram end-time validation with configured PSD: UI rounding up to one sample is accepted, actual file overflow is rejected.
- Kept the previous spectrogram visible while a refresh is in flight or a newer request fails.
- Clamped the requested 30-second spectrogram interval at the file end while retaining the minimum four-second analysis interval.

## Verification

- Frontend Vitest suite: passed.
- `vue-tsc --noEmit`: passed.
- Backend targeted tests could not be collected in the current environment because the existing `mne` dependency is not installed.
