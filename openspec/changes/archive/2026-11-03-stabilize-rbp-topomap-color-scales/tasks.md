## 1. Lock color-scale contract

- [x] 1.1 Add tests for fixed ratio bounds, first-window absolute calibration, session/band isolation, and clamping.
- [x] 1.2 Reset the scale state when the Review analysis session changes and retain per-band bounds during that session.
- [x] 1.3 Render legend endpoints and contours using the selected stable bounds.
- [x] 1.4 Run focused WPF tests, build the desktop client, validate this change strictly, and run `git diff --check`.
- [x] 1.5 Manually verify ratio labels remain `0–1` and absolute labels stay unchanged while playback advances (user confirmed playback acceptance on 2026-11-03).
