# Tasks

## 1. Live RBP contract and scientific validation

- [x] 1.1 Define versioned live-analysis input/result schemas for session identity, V/float64 sample-major data, sampling rate, ordered EEG channels, sample-counter range, result units, quality, provenance, and structured failures; verify schema tests reject mismatched shape, channels, units, and unsupported Nyquist.
- [x] 1.2 Add deterministic aligned-window comparisons between the live preprocessing path and offline RBP for startup, steady state, window boundaries, and supported notch settings; document whether the live path can claim `official-rbp-v4`, and verify any non-equivalence yields a distinct live identity without changing offline fixtures.
- [x] 1.3 Add live sample-source tests for session isolation, gaps, overlap/reset, buffer overwrite, and recovery from contiguous samples; verify invalid windows never receive interpolated or zero-filled data.

## 2. Backend live execution

- [x] 2.1 Implement session-scoped RBP live execution using the bounded live sample source and backend-owned RBP formulas/preprocessing; verify five-band values, channel order, units, quality annotations, and provenance against deterministic backend tests.
- [x] 2.2 Implement bounded scheduling, cancellation, latest-result ownership, and structured warm-up/overload/unavailable/failure states; verify queue overload invalidates affected windows and no live result is persisted as an Analysis Run or backtrace artifact.
- [x] 2.3 Add API/service contract tests for explicit message types, stable error codes, acquisition-session identity, stale-session rejection, and cleanup on close; verify existing offline RBP endpoints and Run records remain unchanged.

## 3. Desktop acquisition integration

- [x] 3.1 Add a live-analysis dispatch branch after raw persistence, independent of display filtering and waveform rendering; verify slow, full, and failed analysis queues do not block device reads, raw writes, or waveform updates.
- [x] 3.2 Connect preview and recording lifecycles to backend live-session creation, sample publication, result/status receipt, cancellation, and close; verify counter/trigger columns are excluded and selected EEG order is preserved.
- [x] 3.3 Add desktop tests for gaps, session replacement, stale results, overload, transport failure, and stop/fault cleanup; verify a new acquisition session cannot display a prior session's result.

## 4. Acquisition UI and end-to-end validation

- [x] 4.1 Present live RBP in a temporary acquisition analysis panel using the same RBP chart control and visual styling as review; append sample-aligned channel windows, retain explicit units/quality/status, and clear on session end without binding the offline review coordinator.
- [x] 4.2 Run backend tests and desktop build/test suites for the changed modules; verify offline review and backtrace have no live-result dependency.
- [ ] 4.3 Run simulated-stream integration tests for continuous samples, gaps, overload, and session shutdown, then perform ANT hardware checks for channel metadata and sample-counter continuity; record observed live identity and limitations without claiming clinical validation.
- [x] 4.4 Run `openspec validate acquisition-live-rbp-analysis --strict` and verify all delta specs, tasks, and compatibility statements pass validation.
- [x] 4.5 Reproject retained raw samples through the newly selected montage and start the rolling scheduler at the latest complete window; verify immediate results after montage changes, sample coordinates, gaps, and overwrite behavior with backend tests.
- [x] 4.6 Include the montage revision in live result identity and make the desktop reject stale revisions while accepting complete reprojected windows that begin before the switch boundary.
