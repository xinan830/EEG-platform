# RBP Topomap Color-Scale Validation

Date: 2026-11-03

## Automated checks

- Focused WPF tests for topomap rendering and Review presentation: 17 passed.
- Isolated desktop-client Release build: passed, 0 warnings and 0 errors.
- `openspec validate stabilize-rbp-topomap-color-scales --strict --no-interactive`: passed.
- `openspec validate --all --strict --no-interactive`: 85 passed, 0 failed.
- `git diff --check`: passed.

## Manual acceptance

The user confirmed playback acceptance on 2026-11-03: relative-power endpoints
remain fixed at 0 and 1, and absolute-power endpoints remain stable while
playback advances within the same analysis run.

Absolute-power bounds are presentation calibration from the first ready window
per frequency band and run. Values outside the bounds saturate at the palette
ends. The bounds are not scientific thresholds and do not make colors directly
comparable between separate runs.
