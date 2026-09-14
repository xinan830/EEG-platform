## 1. Contract and persistence

- [x] 1.1 Specify independent spectral reference validation and evidence requirements.
- [x] 1.2 Add a non-destructive SQLite migration and typed optional evidence field to `ValidationRun`.

## 2. Independent implementation

- [x] 2.1 Implement the SciPy-only independent reference calculation without production spectral imports.
- [x] 2.2 Add the read-only recording validation endpoint and stable errors.
- [x] 2.3 Include reference identity, source digest, actual range, environment, tolerance, and pointwise evidence in reports/exports.

## 3. Verification and archive

- [x] 3.1 Test deterministic 10 Hz / 20 Hz synthetic parity, request-order preservation, unavailable quality output, report/export evidence, and regression safety.
- [x] 3.2 Run backend tests, strict OpenSpec validation, frontend typecheck/test/build, and `git diff --check`.
- [x] 3.3 Write validation evidence, archive the change, update the roadmap, commit, and push.
