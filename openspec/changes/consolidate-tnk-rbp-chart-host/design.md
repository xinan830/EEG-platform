## Context

The RBP tab returns one chart panel per EEG channel. Combining all channels
into one SciChart surface made the display difficult to read and increased
render pressure in the observed playback run. The display contract is now one
independent chart per logical panel: each RBP channel owns one surface with its
four percentage bands stacked from 0 to 100.

This change preserves backend-produced points and display semantics: absolute
recording seconds, percent units, channel order, valid/unavailable states,
cursor selection, history reconciliation, and deterministic disposal. It does
not move scientific work into WPF or join waveform and analysis pipelines.

## Goals / Non-Goals

**Goals:**

- Keep one `TnkAnalysisPlotView` and one `SciChartSurface` per logical
  `TnkPlotPanel`.
- Render every RBP channel as its own stacked chart, with the channel title
  above the chart and the four bands in canonical order.
- Coalesce each panel's data notifications into one refresh and schedule that
  refresh at `Background` priority so input and waveform rendering remain
  ahead of analysis charts.
- Preserve zoom/pan, timeline cursor, empty/quality text, and late-history
  reconciliation.
- Subscribe to panel changes only while a chart is loaded and visible.

**Non-Goals:**

- No backend, API, cache, algorithm-version, filter, sample-boundary, or
  scientific-result changes.
- No timer changes, retry logic, compatibility property, or alternate chart
  library.
- No change to waveform playback or acquisition scheduling.

## Decisions

### One chart view per logical panel

`TnkDetailView.xaml` binds `ActivePlots` to an `ItemsControl`; each item creates
one `TnkAnalysisPlotView` with its `Panel`. Non-RBP tabs still produce one
panel, while RBP produces one panel per channel. This preserves the natural
channel layout and avoids inventing a shared y-axis for unrelated channels.

### Native stacked RBP rendering

The view uses `StackedMountainRenderableSeries` for `IsStacked` panels and a
single `StackedGroupId` for the four bands in that panel. RBP values are
already backend-computed percentages, so the view does not normalize,
recalculate, offset, or reorder them. Stacked legends are lightweight text;
interactive visibility toggles remain for non-stacked metric tabs.

### Coalesced panel refresh

Panel property notifications schedule one pending refresh at
`DispatcherPriority.Background`. The refresh snapshots that panel, updates all
series under one `SuspendUpdates()` scope, then updates its cursor once.
Append/rebuild detection remains the source of truth for late windows and
history trimming. Unloaded or hidden panels unsubscribe before their next
notification.

**Alternative rejected:** one shared surface with offset lines or multiple
interactive controls per RBP band. Both changed the RBP visual contract and
increased the observed chart layout cost.

## Risks / Trade-offs

- **[Risk]** Five visible RBP panels own five SciChart surfaces. → Keep each
  panel limited to its four bands, remove RBP checkboxes, and verify UI-thread
  timing in the next playback run.
- **[Risk]** A late recovered window can require a full series rebuild. → Keep
  the existing append planner and rebuild only when the prefix changes, inside
  one suspended update.
- **[Risk]** A panel can receive a notification while hidden. → Unsubscribe on
  unload/visibility changes and refresh from the latest snapshot on reload.

## Migration Plan

1. Restore the panel collection binding and one chart view per panel.
2. Keep the existing stacked-series and append/rebuild logic inside each view.
3. Add the per-panel lifecycle and background-priority refresh boundary.
4. Run focused desktop tests and manually repeat the RBP playback case with
   the existing performance diagnostics. This is engineering validation, not
   clinical validation.
5. Rollback is a source revert; no database, cache, recording, or backend
   migration is needed.

## Open Questions

None. The per-channel chart topology and RBP stacked visual contract are
decided; exact panel layout remains a WPF presentation detail.
