## Implementation

- [x] 1. Confirm backend IAPF input/output contract and static scope.
- [x] 2. Add IAPF to the WPF static allow-list and channel presentation.
- [x] 3. Submit IAPF and display the backend scalar result.
- [x] 4. Add focused WPF tests and retain existing algorithm regressions.
- [x] 5. Validate OpenSpec, WPF build/tests, backend tests, and a local recording round trip.

Validation note: OpenSpec strict validation passed. WPF isolated build succeeded
with 0 warnings and 0 errors; 32 algorithm tests passed. Backend official
algorithm/catalog tests passed (21 tests). A local recording round trip
completed using channel `O1`, range `0-20 s`, returning a completed
`official-iapf-v2` Run with backend value `10.25 Hz` and clean quality.
