## Why

The TNK RBP view creates one `SciChartSurface` and one view/layout subtree per
channel. A single TNK update therefore invalidates and renders several
independent chart hosts, while WPA shows the UI thread spending most of its
time in WPF layout, automation-tree traversal, and render dispatch. The
waveform and backend algorithm paths are already separate; the remaining
root cause is the chart-host topology, so it should be corrected before more
timing or dispatcher patches are added.

## What Changes

- Replace the per-channel TNK RBP chart-host topology with one chart host for
  the selected TNK view and multiple channel series/groups inside that host.
- Keep the existing channel order, time axis, RBP values, units, quality states,
  cursor behavior, history projection, and replay selection semantics.
- Move channel-specific presentation state into the shared host without adding
  another playback clock or scientific calculation in WPF.
- Remove the obsolete per-channel `TnkAnalysisPlotView`/`SciChartSurface`
  instantiation path after the new host is wired to all current callers.
- Add focused desktop checks for channel ordering, series ownership, cursor
  updates, and disposal of the single host.

This is an internal presentation refactor. It does not change backend APIs,
algorithm versions, scientific preprocessing, stored results, or public units.

## Capabilities

### New Capabilities

None. This change introduces no new user-facing or public data capability.

### Modified Capabilities

None. Existing display requirements remain unchanged; only the internal chart
host topology changes.

## Impact

- **Desktop code:** `TnkWorkbenchPresentation`, `TnkAnalysisPlotView`, the TNK
  detail XAML, and their view-model bindings will be updated together.
- **Runtime behavior:** fewer WPF layout/render subtrees and one SciChart
  lifecycle per selected TNK view; waveform playback and backend computation
  remain independent.
- **Compatibility:** existing recordings, backend results, channels, units,
  and saved provenance remain readable. The old internal per-channel host path
  is removed rather than retained as a fallback.
- **Scientific contract:** unchanged. No algorithm formula, filter, sample
  boundary, time base, channel order, or quality interpretation changes.
- **Validation:** this requires focused desktop tests and a manual WPA/UI
  reproduction. It is engineering/performance validation, not clinical
  validation.
- **Non-goals:** changing dispatcher priority, adding a cache/version bypass,
  changing backend scheduling, or optimizing waveform acquisition.
