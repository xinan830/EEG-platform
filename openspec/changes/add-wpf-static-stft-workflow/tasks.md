## Implementation

- [x] 1. Confirm backend static STFT axes, units, quality, and preview limit against contract tests.
- [x] 2. Add WPF static STFT selection, input validation, submission, and stale-preview clearing.
- [x] 3. Parse and draw a bounded time-frequency preview with explicit axes, color unit, and unavailable cells.
- [x] 4. Add focused WPF tests for request, shape/units/null handling, failures, and PSD regression.
- [x] 5. Validate OpenSpec, WPF tests/build, backend tests, and a permitted local recording round trip; leave graphical acceptance explicit.

Graphical WPF acceptance remains a user-facing verification step: the backend Run `affaad45480b41d7a95f1ac48da0da1b` completed for the supplied recording, channel `O1`, and range `0–20 s`; the WPF bitmap still needs visual confirmation in the running desktop client.
