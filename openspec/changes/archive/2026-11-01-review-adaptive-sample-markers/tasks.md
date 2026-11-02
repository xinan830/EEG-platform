## 1. Display Projection

- [x] 1.1 Add a pure, bounded marker projection/visibility policy using real review-frame samples and the 5 DIP threshold.
- [x] 1.2 Add focused tests for threshold boundaries, finite samples, sample-counter gaps, and exclusion of extrema-only data.

## 2. Review UI

- [x] 2.1 Add an off-by-default point-and-line toggle to the review toolbar and expose its state to the waveform renderer.
- [x] 2.2 Render independent SciChart point markers from bounded frame samples while retaining existing continuous line rendering and gap behavior.
- [x] 2.3 Re-evaluate marker visibility on viewport and plot-size changes without modifying raw, filter, montage, or analysis data.
- [x] 2.4 Add subsecond timebase options to the review page without removing existing wider timebases.

## 3. Validation

- [x] 3.1 Run focused desktop review tests and build the WPF desktop project.
- [x] 3.2 Run strict OpenSpec validation and `git diff --check`; record validation scope and limits.
- [x] 3.3 Verify supported subsecond timebases with focused display-setting tests.

Validation record: `ReviewSampleMarkerProjectionTests` passed 6/6 using an isolated temporary output directory; the WPF desktop project built with 0 errors. Strict OpenSpec validation and `git diff --check` passed. The default output directory was locked by a running `BrainPlatform.Desktop` process, so verification used isolated output paths. The full Review test suite and clinical validation were not performed.

Subsecond timebase follow-up: `WaveformDisplaySettingsTests` passed 15/15 and rebuilt the WPF project in an isolated output directory. `openspec validate review-adaptive-sample-markers --type change --strict` and `git diff --check` passed. Runtime visual acceptance remains pending.
