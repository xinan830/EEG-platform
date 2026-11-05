## 1. Contracts and library validation

- [x] 1.1 Verify supported Python runtime/dependency and define API/package contract tests.
- [x] 1.2 Validate real-recording EDF/BDF write/read-back with MNE, exact samples/rate, channel mapping, quantization and source preservation.

## 2. Backend and desktop

- [x] 2.1 Implement bounded raw export, provenance, events, cancellation, failure cleanup and concurrency admission.
- [x] 2.2 Wire WPF raw registration/download, transactional file publication, progress/cancel and remove C# writer/dependency with replacement tests.

## 3. Verification

- [x] 3.1 Run backend and desktop targeted suites/build, strict OpenSpec validation, file-size policy and diff checks.
- [x] 3.2 Record acceptance outputs, measured limitations and runtime migration instructions.

Evidence: `docs/validation/2026-11-05-python-signal-export.md`. Export-focused checks passed; unrelated broader desktop failures and pre-existing full-repository size-policy failures remain explicitly documented. WPF manual acceptance and release-package validation are not claimed.
