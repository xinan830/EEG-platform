# Dynamic spectral results UI tasks

## 1. API contract

- [x] Define bounded structured-preview response types and validation.
- [x] Add checksum-verified preview endpoint for structured PSD/STFT artifacts.
- [x] Preserve artifact identity, units, axes, window states, and null cells.
- [x] Add API/service tests for structured preview and checksum failure paths.

## 2. Frontend integration

- [x] Add typed API client for structured previews.
- [x] Add PSD frequency-series preview rendering with backend units and states.
- [x] Add STFT time-frequency preview rendering with blank unavailable cells.
- [x] Keep existing scalar and legacy spectrum/spectrogram rendering unchanged.
- [x] Add loading, empty, error, and export states in the result workbench.

## 3. Verification

- [x] Add frontend/API tests for preview requests and structured result rendering.
- [x] Run backend tests, frontend tests/build, OpenSpec strict validation, and
  diff checks.
- [x] Record preview limits, artifact identity, and rollback commit in a
  validation report.
