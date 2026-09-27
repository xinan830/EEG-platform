## Implementation

- [x] 1. Confirm backend Band Ratio input/output contract and Nyquist rule.
- [x] 2. Add static numerator/denominator band inputs and validation to WPF.
- [x] 3. Submit Band Ratio and display the backend scalar result.
- [x] 4. Add focused WPF tests and retain PSD/STFT/RBP/Peak Frequency regressions.
- [x] 5. Validate OpenSpec, WPF build/tests, backend tests, and a local recording round trip.

Validation note: OpenSpec strict validation passed. WPF isolated build succeeded
with 0 warnings and 0 errors; 28 algorithm tests passed. Backend contract and
catalog tests passed (13 tests). A local recording round trip completed using
recording `wpf-b686efc5-f5ac-46e2-9a38-351bf2ef6960-4fb4453d71ae`, channel
`O1`, range `0-20 s`, numerator `4-8 Hz`, denominator `8-13 Hz`, returning a
completed `official-band-ratio-v1` Run with backend value `0.0957717291 ratio`.
