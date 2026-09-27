## Implementation

- [x] 1. Confirm backend FAA input/output contract and static-only rule.
- [x] 2. Add F3/F4 channel selectors and validation to WPF.
- [x] 3. Submit FAA and display the backend scalar result.
- [x] 4. Add focused WPF tests and retain existing algorithm regressions.
- [x] 5. Validate OpenSpec, WPF build/tests, backend tests, and a local recording round trip.

Validation note: OpenSpec strict validation passed. WPF isolated build succeeded
with 0 warnings and 0 errors; 30 algorithm tests passed. Backend official
algorithm/catalog tests passed (21 tests). A local recording round trip
completed with F3/F4 source channels and returned a completed
`official-faa-v1` Run. The scalar was correctly `null` with structured
`FAA_QUALITY_GATE_FAILED` evidence because the selected range had too few clean
paired epochs; WPF must preserve that unavailable result rather than display a
fabricated number.
