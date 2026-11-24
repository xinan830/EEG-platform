## Contract And Implementation
- [x] Add shared formatter/settings/context tests before integration.
- [x] Add shared segmented selector and time-axis presentation.
- [x] Integrate review labels, navigator, cursor and marker presentation.
- [x] Integrate algorithm local origin, time axes and shared range/cursor text.
- [x] Verify unchanged analysis input and playback state.

## Validation
- [x] Run desktop tests and Release build (480 passed).
- [x] Validate OpenSpec strictly and check whitespace/file-size policy.
- [x] Record results and manual acceptance instructions in the validation report.

Real recording/GPU/window acceptance remains a user acceptance step, not an
automated-test claim. See `docs/validation/2026-10-10-recording-time-display-report.md`.

## Fractional Origin Regression
- [x] Add regressions for review canvas tick invariance and millisecond clock labels.
- [x] Keep review ticks aligned to recording seconds in both modes; preserve live acquisition policy.
- [x] Rerun desktop tests (484 passed), Release build and contract checks; update acceptance evidence.
