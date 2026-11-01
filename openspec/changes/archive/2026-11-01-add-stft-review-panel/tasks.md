## 1. Shared boundary
- [x] 1.1 Generalize the existing Review dynamic adapter and preserve output, units, identities and failure evidence; test routing for both algorithms.
- [x] 1.2 Add STFT Review configuration and reuse the STFT parser with absolute-time, quality and malformed-result tests.

## 2. Review composition
- [x] 2.1 Add one-active-panel lifecycle with cancellation and switch/close race coverage, retaining the shared recording and time coordinator.
- [x] 2.2 Add STFT menu/toolbar entry and compact channel/heatmap panel, preserving time formatting, units and failure details.

## 3. Verification
- [x] 3.1 Run focused desktop regression tests and isolated WPF build; validate OpenSpec strictly, diff whitespace and touched-file size policy.
- [x] 3.2 Record integration points, validation evidence and manual acceptance steps, explicitly identifying unverified GUI/performance behavior.

## 4. Real-recording defect correction
- [x] 4.1 Restrict STFT montage mappings to the selected input, preserve registered role/unit/identity and the full selector list, and cover initial selection, channel changes and montage changes.
- [x] 4.2 Run focused regression tests, rebuild the desktop and record the correction and remaining GUI acceptance gap.

## 5. Accumulating time display
- [x] 5.1 Reuse heatmap history for Review, deduplicate overlapping times, reset backward/configuration/failure paths and verify real presenter-to-chart bindings.
- [x] 5.2 Run focused desktop tests, build and specification checks; update acceptance evidence for accumulating history.

## 6. Compact STFT presentation
- [x] 6.1 Place channel selection in the panel header beside dock/close actions, remove the evidence expander, and keep the unit with the heatmap color scale.
- [x] 6.2 Verify header alignment, chart visibility, failure status, build and OpenSpec consistency.
- [x] 6.3 Remove STFT axis titles, tighten the vertical tick rail and bottom axis area, and verify the compact plot layout.

Evidence: `docs/validation/2026-11-01-stft-review-panel.md`. After the accumulating-history update, focused tests: 79 passed; rebuilt WPF: 0 errors, 3 existing compatibility warnings; OpenSpec/diff checks passed and all four files touched by this update remain below 400 lines. Broader regression has two failures on unchanged waveform/navigation paths; full repository size check retains the unchanged on-demand service overage. Real-recording GUI/performance acceptance is pending.
