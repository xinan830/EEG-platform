# Tasks

## 1. Confirmation and lifecycle

- [x] 1.1 Add regressions for cancelled ICA review, changed montage/bads, reset and stale responses; verify the failures before implementation.
- [x] 1.2 Separate pending and confirmed ICA state, centralize input invalidation and cancel/reset lifecycle; verify new state tests pass.
- [x] 1.3 Clear and version preview requests, reject stale publication and suppress stale playback overlays; verify delayed/failing preview tests.

## 2. Scientific display contracts

- [x] 2.1 Distinguish selected policies from displayed processed samples and expose actual inspection coverage; verify badge and long-recording tests.
- [x] 2.2 Preserve extrema and NaN gaps in waveform previews with documented coordinates and API DTO tests.
- [x] 2.3 Reject unsupported bipolar ICA scalp placement and verify raw/REF/AVG compatibility tests.
- [x] 2.4 Expose bounded STFT/TNK removal limitations in Review and verify its presentation contract.

## 3. Integration verification

- [x] 3.1 Run focused backend and desktop regressions, Release compilation, OpenSpec validation and changed-file size checks; document results and unrelated failures.

Evidence: `docs/verification/review-preprocessing-state-2026-11-25.md`.

## 4. Follow-up correctness

- [x] 4.1 Preserve confirmed ICA and previews on read-only rescans; invalidate only changed scientific inputs and keep initial ICA status truthful.
- [x] 4.2 Suspend scientific overlays throughout temporary display-montage transitions, reject stale responses and resume on return without changing analysis or ICA.
- [x] 4.3 Derive interpolated output labels from complete formula dependencies, including negative and average references.
- [x] 4.4 Preserve ICA mini-waveform extrema, gaps, units and explicit time coordinates through backend and desktop rendering.
- [x] 4.5 Run focused regressions, Release compilation, contract and size checks; record actual coverage and limitations.
