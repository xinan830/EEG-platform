- [x] Add explicit filter/output range contract to spectral loading.
- [x] Update official PSD static and dynamic execution and provenance.
- [x] Add regression tests for dynamic filter settings and fixed output axis.
- [x] Run backend tests, strict OpenSpec validation, and diff checks.

Verification (2026-10-18): PSD frequency-contract, spectrum-service,
algorithm-frequency-contract and PSD on-demand capability tests: 82 passed.
The preceding full backend suite passed 740 tests; backend code is unchanged
since that run (commit afe9061). Change strict validation and git diff --check
passed. This records engineering regression evidence, not clinical validation.
