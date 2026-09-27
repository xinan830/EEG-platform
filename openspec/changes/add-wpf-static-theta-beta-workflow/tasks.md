## Implementation

- [x] 1. Confirm backend Theta/Beta input/output contract and static scope.
- [x] 2. Add Theta/Beta to the WPF static allow-list and channel presentation.
- [x] 3. Submit Theta/Beta and display the backend scalar result.
- [x] 4. Add focused WPF tests and retain existing algorithm regressions.
- [x] 5. Validate OpenSpec, WPF build/tests, backend tests, and a local recording round trip.

Validation note: OpenSpec strict validation passed. WPF isolated build succeeded
with 0 warnings and 0 errors; 34 algorithm tests passed. Backend official
algorithm/catalog tests passed (21 tests). A local recording round trip
completed using channel `O1`, range `0-20 s`, returning a completed
`official-theta-beta-v2` Run with backend value `0.3856407059 ratio` and clean
quality.
