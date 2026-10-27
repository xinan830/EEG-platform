## Why

Review users need to inspect how a continuous EEG trace relates to its discrete samples. Showing a circle for every sample at every zoom level creates a dense, misleading display, so the review workspace needs an explicit point-and-line mode that adds markers only when real samples are visually separable.

## What Changes

- Add a review toolbar toggle for point-and-line mode; continuous lines remain the default.
- Render sample markers from the current bounded projected review samples, independently of the extrema-reduced line and display-level cache.
- Automatically hide markers when adjacent real samples are closer than the display readability threshold, retaining the continuous waveform.
- Preserve existing gap handling, time coordinates, montage, filtering, raw samples, and analysis behavior.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/recording-review`: specify optional sample markers and their adaptive visibility in the review waveform.

## Impact

- Affected code: WPF review toolbar and SciChart waveform rendering; focused review waveform tests.
- APIs, persistence, backend algorithms, and dependencies: unchanged.
- Compatibility: existing sessions continue to show continuous lines unless the user enables the new toggle.
- Non-goals: clinical interpretation, changing sample data, drawing every point at all zoom levels, or changing scientific filtering/montage behavior.
- Scientific-contract impact: display-only; marker coordinates and values come from the same already-projected V/float64 display samples as the bounded review frame. They are not analysis inputs.
- Migration/rollback: no data migration; removing the toolbar toggle and marker layer restores the existing line-only behavior.
