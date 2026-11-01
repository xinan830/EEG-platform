## Context

Review has one active algorithm binding and one playback coordinator. Its existing generic adapter requests one target window; TNK additionally publishes canonical intermediate windows through the opt-in playback-delivery endpoint so stateful children remain continuous.

## Decisions

1. Add a TNK-specific Review adapter that owns one bounded target coordinator and one event consumer. It uses the existing `IAlgorithmClient` APIs and does not borrow the standalone TNK catalog or its playback state.
2. Generate the configuration through the existing algorithm run configuration builder. The five role mappings are explicit, distinct, and limited to the active registered montage/source channels. Missing roles are errors; no proxy channel is inferred.
3. Reuse the existing TNK event response, `TnkResultChartProjection`, `TnkWindowChartHistory`, and `TnkAnalysisPlotView`. The Review presenter filters projected points by its captured opening time; it does not truncate backend replay or alter stateful calculation inputs.
4. A backward seek starts a fresh delivery generation so incompatible later-state checkpoints/results cannot flow into the Review projection. Forward playback retains the delivery identity and consumes every ordered event.
5. Panel replacement, configuration change, close, and Review disposal cancel polling, reset the owned target coordinator, and close the delivery session. Waveform playback remains independent.
6. Delivery discontinuities and child unavailability remain visible as gaps/reasons. `tnk_faa` keeps its existing unavailable semantics when the required complete event segment is absent.
7. When TNK is active, Review docks its algorithm panel below the waveform preview. Its compact header omits the status row and places the five role selectors beside the dock and close controls. The content is split into a left algorithm area and right meditation summary; the nine metric tabs and selected timeline plot stay within the left column. Summary metrics use separate compact rows with clipped long values so fields cannot overlap. The aperiodic exponent is dimensionless and is displayed without appending the backend's `exponent` type label as a physical unit. Other algorithm panels retain the existing side-by-side dock. Chart points, recent-valid cards, and score-state duration summaries are limited to results at or after the captured panel-open cursor; earlier replay windows remain internal state restoration only.

## Risks

- The backend may replay windows before the panel-open cursor; this is required for state continuity and can affect first-result latency, but those earlier points must not appear in the chart.
- A five-role montage may be incomplete or map duplicate source channels; the panel must report that configuration error rather than silently substituting channels.
- Event-buffer overflow can leave visible history incomplete; the panel must retain an explicit coverage state instead of implying a complete timeline.

## Validation

- Focused tests cover role/configuration validation, event order and deduplication, delivery reset on backward seek, chart start cutoff, and close/switch cancellation.
- Build the desktop app and run the focused Review tests. Manual playback, seek, role-change, panel-close, and waveform-independence checks on a local recording remain a separate acceptance step.
