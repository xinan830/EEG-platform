## Implementation

- [x] 1. Confirm backend Peak Frequency input/output contract and Nyquist rule.
- [x] 2. Add static frequency-band inputs and validation to WPF.
- [x] 3. Submit Peak Frequency and display the backend scalar result.
- [x] 4. Add focused WPF tests and retain PSD/STFT/RBP regressions.
- [x] 5. Validate OpenSpec, WPF build/tests, backend tests, and a local recording round trip.

Validation note: WPF isolated build succeeded with 0 warnings and 0 errors;
24 algorithm tests passed. Backend Peak Frequency/catalog tests passed (13
tests). A local recording round trip completed through the running API using
recording `wpf-b686efc5-f5ac-46e2-9a38-351bf2ef6960-4fb4453d71ae`, channel
`O1`, range `0-20 s`, band `1-30 Hz`, and returned a completed clean Run with
backend scalar value `10.25 Hz`.
