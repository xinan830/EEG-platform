## Implementation

- [x] Define the range and dynamic-window contract, including compatibility and non-goals.
- [x] Add long-range and out-of-recording regression coverage.
- [x] Remove the 120-second query upper bound from legacy spectrum and spectrogram endpoints.
- [x] Reject service requests beyond recording duration instead of truncating them.
- [x] Remove stale 120-second dynamic-window schema metadata.
- [x] Remove the fixed 30-second frontend Run-polling cutoff.
- [x] Align time-contract documentation.
- [x] Run focused and full backend tests, frontend tests/build, OpenSpec strict validation, and `git diff --check`.
