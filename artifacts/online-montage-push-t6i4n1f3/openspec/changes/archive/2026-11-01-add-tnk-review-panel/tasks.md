## 1. Review configuration and adapter

- [x] 1.1 Add strict five-role TNK Review configuration based on the current catalog policy and selected montage.
- [x] 1.2 Add a Review-owned ordered TNK adapter with delivery polling, generation validation, backward-seek reset, and deterministic close.
- [x] 1.3 Add focused tests for role validation, configuration identity, ordered event projection, and delivery lifecycle.

## 2. Review panel and chart

- [x] 2.1 Add TNK Review presentation using the existing backend projection/history/chart components and visible-start cutoff.
- [x] 2.2 Register TNK in Review menu, toolbar, panel composition, and configuration-change lifecycle.
- [x] 2.3 Add regressions for open-at-cursor chart start, missing/duplicate roles, gap visibility, stale event rejection, and close/switch behavior.
- [x] 2.4 Dock the TNK panel below the Review waveform preview; use a compact header with role selectors beside dock/close controls, keep the nine metric tabs and chart in the left column beside the right summary, and present summary values as separate rows; limit summary totals to results after panel open.
- [x] 2.5 Keep the Review TNK chart right edge at the latest published result and leave the shared playback cursor visible only on the waveform.

## 3. Validation

- [x] 3.1 Run focused Review tests and a desktop build; run OpenSpec strict validation and `git diff --check`.
- [x] 3.2 Record manual local-recording acceptance for playback, seek backward/forward, role mapping, close/switch, and waveform independence.
  - 2026-11-01: User completed the local-recording acceptance and reported no remaining issue for the shared waveform cursor and TNK Review chart behavior.
