## Implementation

- [x] 1. Confirm backend static STFT axes, units, quality, and preview limit against contract tests.
- [x] 2. Add WPF static STFT selection, input validation, submission, and stale-preview clearing.
- [x] 3. Parse and draw a bounded time-frequency preview with explicit axes, color unit, and unavailable cells.
- [x] 4. Add focused WPF tests for request, shape/units/null handling, failures, and PSD regression.
- [x] 5. Validate OpenSpec, WPF tests/build, backend tests, and a permitted local recording round trip; confirm the rendered WPF time-frequency image.

Validation: backend Run `affaad45480b41d7a95f1ac48da0da1b` completed for the supplied recording, channel `O1`, and range `0–20 s`. The user confirmed the WPF time-frequency image in the running desktop client is correct.
