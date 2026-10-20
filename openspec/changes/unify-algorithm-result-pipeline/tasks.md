## 1. Contract Inventory

- [x] 1.1 Inventory current full-run response fields, array metadata, provenance, identity checks, and failure codes for PSD and STFT.
- [x] 1.2 Define typed descriptor models and a registry for arrays, axes, units, shapes, time semantics, channel order, and compatibility versions.
- [x] 1.3 Add descriptor validation that rejects missing or contradictory scientific semantics before any window is exposed.

## 2. Shared Reader

- [x] 2.1 Extract one load-time structured-preview validator and immutable absolute-sample window index from the existing full-run path.
- [x] 2.2 Route cursor selection, state mapping, failure mapping, provenance normalization, and array slicing through the shared contract.
- [x] 2.3 Preserve existing response fields and add explicit compatibility source/version diagnostics without changing scientific values.
- [x] 2.4 Keep Run/artifact reuse identity checks and on-demand fallback behavior unchanged for cache misses and integrity failures.

## 3. Algorithm Adapters

- [x] 3.1 Register the PSD descriptor and migrate PSD full-run preview reads to the shared reader.
- [x] 3.2 Register the STFT descriptor and migrate STFT full-run preview reads, including relative time-axis provenance, to the shared reader.
- [x] 3.3 Reduce PSD/STFT presenters to algorithm-specific projection and formatting only; remove duplicated transport rules.

## 4. Verification

- [x] 4.1 Add shared contract tests for complete, partial, rejected, unavailable, malformed, reused-run, and legacy artifacts.
- [x] 4.2 Add PSD/STFT parity tests proving identical identity, state, range, failure, unit, and provenance behavior through the shared path.
- [x] 4.3 Run focused WPF tests in an isolated output directory and perform manual cursor-seek acceptance on a completed local recording.
- [x] 4.4 Run OpenSpec strict validation, relevant backend/frontend checks, and `git diff --check`; document any environment-limited checks.
