# Live Spectral Views Validation — 2026-11-23

The live RBP and STFT chart activation boundary now uses the sample-counter-derived activation time while preserving rolling calculation windows and session-relative coordinates.

- Focused STFT, review heatmap, and live spectral presentation tests: 62 passed, as recorded in the completed OpenSpec task.
- `openspec validate add-live-spectral-views --strict`: passed.
- `openspec validate --specs`: 90 passed, 0 failed; existing repository warnings remain outside this capability.
- `git diff --check`: passed.
- A rerun of the focused desktop tests was blocked while MSBuild attempted to replace `BrainPlatform.Desktop.exe`, locked by running processes 45616 and 32432. The processes were left untouched.
- Rendering with the ANT device and long-duration hardware acquisition were not verified.
