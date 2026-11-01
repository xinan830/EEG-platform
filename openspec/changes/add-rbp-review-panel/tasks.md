## 1. Contracts and adapter

- [x] 1.1 Remove the PSD-only Review commands, panel, parameters and adapter registration while retaining PSD in the backend and separate algorithm workspace.
- [x] 1.2 Add the explicit Review algorithm selection/registration boundary for RBP without embedding the catalog workspace.
- [x] 1.3 Implement `RbpReviewAnalysisAdapter` using the existing registered-recording, montage and dynamic-window contracts.
- [x] 1.4 Add adapter tests for recording identity, current RBP scientific identity, channel order, units and unsupported capability.

## 2. Review presentation

- [x] 2.1 Add an RBP Review panel/presenter for multi-channel ratios, actual window range and per-channel quality warnings.
- [x] 2.2 Bind RBP to the existing Review analysis binding and time coordinator; keep waveform playback and panel lifecycle independent.
- [x] 2.3 Add stale-response, close, recording-switch and rapid-cursor replacement tests.
- [x] 2.4 Correct failed performance acceptance: ignore status-only projection updates, retain per-channel history/series, append actual-window points, and virtualize offscreen channel charts.
- [x] 2.5 Add regressions for repeated results, channel object/series identity, backfill, session reset, detach, binding notification fan-out, and channel-list recycling.
- [x] 2.6 Remove the Review summary bar chart and percentage legend; retain actual-window text and individual channel history charts.
- [x] 2.7 Compact channel charts to 120 pixels, remove normal headings/status text, and move range/quality details to tooltips while preserving empty/failure presentation.
- [x] 2.8 Hide axis titles in Review channel charts while retaining tick marks, tick values and percentage formatting; preserve standalone RBP titles.

## 3. Validation

- [x] 3.1 Run focused WPF tests and build; run backend tests only if backend files change.
- [x] 3.2 Run OpenSpec strict validation and `git diff --check`.
- [ ] 3.3 Manually verify one recording: open RBP in Review, seek/play, inspect actual window and channel quality, close panel, and confirm waveform remains usable.

Manual acceptance failed with UI freezes on 2026-10-31. Tasks 2.4–2.5 address
the confirmed presentation defect; 3.3 must be repeated with the corrected WPF build.
