## 1. Result And Scientific Contracts

- [x] 1.1 Trace the current PSD/RBP saved-window result and cache ownership, then finalize a typed custom projection contract that can serve arbitrary intervals without re-reading raw EEG or rerunning Welch during cursor interaction.
- [x] 1.2 Define and test actual frequency-bin boundaries, outer supported-domain behavior, interval integration, PSD mean-density, RBP absolute power, the 1–50 Hz denominator, RMS units, quality, channel order, and source provenance before implementation changes.
- [x] 1.3 Add compatibility contract tests proving existing five-band PSD/RBP values, topomap responses, and historical saved results keep their current behavior; record any RBP current-version/Definition update required without adding an algorithm ID or fallback.

## 2. Backend Projections

- [x] 2.1 Expose backend-owned per-bin PSD and RBP absolute/relative metrics for every ordered channel from a matched current analysis window; report the actual frequency grid, bin intervals, units, quality, and provenance.
- [x] 2.2 Add typed custom-range projection using the same saved-window source, boundary-aware integration, full 1–50 Hz RBP denominator, range-average PSD density, and backend RMS; reject unsupported or invalid ranges with stable errors.
- [x] 2.3 Verify point/range projections do not call raw signal readers or Welch execution again, prepare spatial coordinates, or alter existing official five-band result arrays; test missing/quality-failed channels and mismatched window identities.

## 3. Review Interaction

- [x] 3.1 Provide `OpenCustomFrequencyInspectionCommand`, panel-kind selection, and the inspection ViewModel; connect the visual entry and panel after the user's subsequent UI request.
- [x] 3.2 Expose point and range-endpoint snapping to the current PSD frequency axis, selected-channel PSD curve data, stable all-channel numeric rows, units, quality and unavailable details for UI binding.
- [x] 3.3 Bind the business state to the current matched PSD/RBP playback window; retain row and chart ownership, debounce and cancel obsolete range projections, and ensure seek/ticks never wait for the panel.
- [x] 3.4 Add desktop business/HTTP tests for point and range-endpoint bin snapping, range edits, channel selection, collection-view sorting, numeric values, quality failures, stale responses, window changes, close cancellation, and playback independence.
- [x] 3.5 Add the separate toolbar/menu entry, two-pane XAML, single cursor, two range boundaries/inputs, curve tooltip and sortable/scrollable table; the user requested the initial UI after business delivery.
- [x] 3.6 Restore exhaustive panel-template registration coverage; add rendered-control tests for cursor/range drag events, selection, scroll/sort, numeric/null and unit bindings, chart-series reuse and unload detachment. Verify curve/cursor rendering with application license initialization in a temporary preview harness; physical pointer/tooltip acceptance remains in 4.2.

## 4. Verification And Delivery

- [x] 4.1 Run relevant backend and desktop tests, full affected Review regressions, strict OpenSpec validation, `git diff --check`, and code-size checks; record any baseline/environment failures.
- [ ] 4.2 Complete manual review with a local recording: compare selected PSD points and RBP outputs against backend responses, drag both modes, switch/sort channels, seek/play during range loading, and inspect unavailable states.
- [x] 4.3 Update the validation report with tested frequency grid, actual bin-width examples, RBP denominator and integration behavior, result provenance, and remaining limitations.

## 5. Saved-Source Performance Repair

- [x] 5.1 Separate public window reads from backend-only RBP spectral projection fields; preserve source validation, five-band/scalar/topomap behavior and immutable result provenance.
- [x] 5.2 Add byte- and entry-bounded validated artifact reuse with file-change invalidation, concurrent-read coalescing and isolated caller ownership; batch per-bin integration across channels without changing formulas.
- [x] 5.3 Verify selective decoding, warm reads, eviction, corruption, project scope and scientific equivalence; measure ordinary/custom response sizes and cold/warm projection time, then update the validation report.

## 6. Real-Recording Lifecycle Repair

- [x] 6.1 Correlate real desktop/backend logs; reproduce fractional-cursor first-open gating and slow-source starvation without changing PSD/RBP science or topomap scheduling.
- [x] 6.2 Give numeric-panel updates one paired source/projection owner and one newest pending playback target; retain explicitly dated published values while loading, invalidate on seek/drag/configuration/close, and add focused first-open/backpressure regressions.
- [x] 6.3 Run affected desktop/backend regressions, strict OpenSpec and file-size/whitespace checks; record actual results and the unresolved real-playback performance boundary.

Implementation handoff: `docs/validation/2026-11-06-review-custom-frequency-inspection.md`. Archived on 2026-11-06 under the user's explicit submit/push/archive instruction. Task 4.2 remains incomplete; archiving does not assert real-recording/manual acceptance.

## 8. PSD Hover Frequency Alignment

- [x] 8.1 Snap the hover axis label and tooltip to the same PSD series sample without interpolation; verify the chart interaction and record remaining manual acceptance.

## 7. Measured Window Infrastructure Repair

- [x] 7.1 Batch snapshot reads and bound validated catalogue reuse by manifest and complete database content; verify corruption, changes, rebinding, ID deduplication and caller isolation.
- [x] 7.2 Move request planning outside the admission gate while preserving atomic capacity, duplicate-window, shutdown and recovery behavior.
- [x] 7.3 Replace repeated cancellation connection/record reads with a worker-owned atomic state/lease reader; verify external cancellation, token/expiry fencing and disposal.
- [x] 7.4 Run focused lifecycle, API, scientific projection and performance regressions; record synthetic before/after costs, code-size checks and real-playback limitations. The repair-focused regression set passed 61 tests. Strict OpenSpec CLI validation passed at archival; post-change live playback acceptance remains outstanding.

## 9. Direct Cursor And Whole-Range Dragging

- [x] 9.1 Give the chart one mouse-capture owner for direct single-press point/boundary dragging and whole-fill translation; keep Hz width, bin snapping, atomic range publication and existing backend projection ownership.
- [x] 9.2 Verify point/label/handle/fill interaction, limits, nonuniform bins, capture/model/unload cancellation and stable chart data; record tests and remaining physical-pointer acceptance.

## 10. Frequency Input Steppers

- [x] 10.1 Show compact hover/focus step buttons on point and range inputs; use real bins, preserve text validation and existing projection lifecycle, and verify limits and keyboard interaction.
