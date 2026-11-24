## Implementation

- [x] 1. Confirm the backend RBP scalar result contract and units.
- [x] 2. Allow static RBP submission while keeping dynamic RBP unavailable.
- [x] 3. Format backend band shares and avoid structured-preview requests for RBP.
- [x] 4. Add focused WPF tests and retain PSD/STFT regression coverage.
- [x] 5. Validate OpenSpec, WPF tests/build, backend RBP tests, and a local recording RBP round trip.

Validation note: local recording `wpf-b686efc5-f5ac-46e2-9a38-351bf2ef6960-4fb4453d71ae`, channel `O1`, range `0–20 s`, produced a completed RBP Run with clean quality and four ratio values. Dynamic RBP remains intentionally unavailable.
