## Context

See `proposal.md` for motivation and `specs/desktop/review-spectral-values/spec.md` for user-visible behavior. The existing five-band inspection remains a separate entry. The new panel combines a single-channel PSD curve with same-window metrics for every ordered analysis channel. Scientific formulas stay in the backend.

Constraints:

- Single-frequency selection is a view selection over the actual current PSD frequency grid; the displayed value is the selected grid frequency, not an arbitrary interpolated frequency.
- Custom range endpoints snap to frequencies in the current common PSD/RBP grid inside 1–50 Hz. RBP power uses the existing backend boundary-aware band integration policy with those actual Hz endpoints; a range must be fully supported.
- Single-bin power uses the frequency interval represented by that bin: midpoints between adjacent frequency samples, with the outer edge bounded by the supported PSD interval. Its RMS is equivalent bin RMS, not sinusoidal peak amplitude.
- Custom relative power uses the selected RBP interval power divided by total RBP power over 1–50 Hz. PSD range density is the PSD integral divided by the selected interval's full width.
- `uV^2/Hz`, `uV^2`, `uV`, and ratio/percent conversions are explicit display/API contracts. The table shows relative power as percent.

## Goals / Non-Goals

**Goals:**

- Add the two-mode interaction and keep all-channel numeric values synchronized with one selected-channel PSD curve.
- Reuse the existing PSD and RBP review window identities, settings, and calculation ownership.
- Keep cursor movement responsive by selecting precomputed backend bin projections and querying only the selected custom-range projection from the current saved window.
- Retain source quality, provenance, explicit units, and unavailable reasons.

**Non-Goals:**

- Replace or remove the existing five-band panel, alter the topomap, or change waveform playback/window settings.
- Add a new official algorithm ID, perform EEG/PSD/RBP calculations in WPF, extrapolate beyond the available grid, or create database migrations.
- Treat single-bin RMS as the amplitude of a sinusoidal component.

## Decisions

- **Use a separate review entry and side panel.** Keep the existing five-band action and view unchanged. The new panel owns only its selection mode, cursor/range, selected channel, and table presentation. Reuse the existing PSD chart interaction/control where it fits; do not couple frequency cursor state to playback time.
- **Snap point mode and both range boundaries to the returned PSD axis.** The UI displays and submits actual grid frequencies for point and range selections. This avoids frontend interpolation and makes the selected values auditable. A free-running interpolated cursor was rejected because it would require another interpolation contract.
- **Represent ranges with two draggable boundaries and numeric inputs.** Dragging and text entry both snap to the nearest supported frequency; equidistant values select the lower frequency. Reject reversed, zero-width, non-finite, or unsupported intervals rather than silently swapping, clipping, or extrapolating them.
- **Keep PSD and RBP as separate named sources.** PSD density always comes from the PSD result. RBP absolute/relative power comes from an RBP-owned backend custom-interval projection using the matched saved-window spectral source; do not derive these metrics from the five existing aggregate band outputs. The projection records its applied interval and denominator. The RBP definition/implementation identity is updated according to the repository's current-version policy without adding an algorithm ID or historical fallback.
- **Make bin and range projection backend-owned.** For a current PSD/RBP window pair, prepare per-bin PSD and RBP metrics once so point-cursor movement is a local selection of backend-computed values. A custom-range change requests a typed RBP/PSD projection against that same saved window; debounce continuous boundary drag, cancel/supersede older requests, and publish only matching selection/window identities. Do not rerun Welch per cursor movement or block playback. If the current result store cannot supply the RBP-side spectrum needed for arbitrary intervals, first extend its current-window projection data contract/storage owner; do not fall back to a second raw-signal calculation path.
- **Keep row identity stable.** Preserve row objects when channel membership/order is unchanged. Sorting is a view concern and does not alter the request's analysis channel order. Selecting a row switches only the single PSD curve. Do not rebuild all channel charts when the selected curve changes.
- **Keep the display auditable.** The chart draws only backend-returned frequencies on the 0–50 Hz display axis and reports the actual point/unit in its tooltip. Gaps remain gaps. Each row exposes concise quality and provenance detail; unavailable values remain null with structured reasons.
- **No persisted recording migration.** Additive API/result projection changes deploy with the desktop consumer. Existing saved five-band results and topomap routes keep their contracts; the custom panel requires the current backend capability and has no map or alternate-calculation fallback. Rollback removes the new entry/API consumer while preserving existing PSD/RBP and five-band behavior.

Alternatives rejected: computing range power in WPF would duplicate scientific integration; reconstructing partial bands from five aggregate RBP values is mathematically insufficient; sending a backend request for every point-cursor movement adds avoidable latency; rerunning a Welch window for each drag couples presentation to scientific execution.

## Risks / Trade-offs

### Paired playback publication repair

Real-recording logs on 2026-11-06 exposed two distinct lifecycle bugs. Opening at 50.352 s requested sample 50352 while the backend correctly returned the aligned [40000, 50000) window; `IsTargetDisplayed` incorrectly prevented the numeric projection. During playback, independent latest-target waiters cancelled the slower RBP observation before publication and required both sources to be at the newest cursor simultaneously. This is not a scientific-source mismatch.

`RecordingReviewViewModel.SpectralWindows` owns both existing source bindings and the current projection as one transaction, with only one newest pending playback sample. It awaits the two existing source tasks, validates the pair's actual range/configuration through `ReviewSpectralSources`, and publishes once. A playback tick never cancels an active pair or awaits it; source windows that finish at different speeds cannot diverge indefinitely. The first-open caller waits for source readiness, not the potentially slow projection, so close/seek and the preparation gate remain independent.

Waiting for the next source/bin projection keeps the previously published curve/rows and actual window label; pending status explicitly identifies that retained window. A terminal source/projection failure clears values. The selected custom range is projected immediately after new bins; only user range edits receive the 180 ms debounce. Point/channel selection remains local. Explicit seek, timeline drag, montage/configuration change and close cancel the pair plus projections and clear displayed values; source/selection revisions reject late responses. Individual PSD/RBP plot and topomap scheduling is unchanged. Backpressure may skip intermediate stateless playback targets when computation exceeds the step, while the scientific window/step and actual displayed timestamps remain unchanged; it makes no hard real-time guarantee.

With performance diagnostics enabled, `review_spectral_pair_perf` records review session, target sample, both window IDs, combined source time, projection time and newest pending sample. This separates a source bottleneck from projection/display delivery in the next real-recording acceptance run.

While a successor bin response is pending, range edits only update the selected bounds. The selected range is projected after that response is published, so its source pair and the displayed bin grid always agree. Starting a timeline drag invalidates projections immediately, before the first preview movement.

### Saved-source performance boundary

- Declare RBP `frequency_hz` and `spectral_psd` as backend-only on-demand projection fields. Persist them once with the existing window, but ordinary ensure/status, scalar and topomap reads must neither decompress them nor return them. Custom-frequency access uses an explicit internal source-read option after the same project-scope check. Do not create another EEG/Welch execution or rewrite historical artifacts.
- Retain an artifact-store-owned cache capped at 32 MiB and 32 entries for validated decoded results. Key reuse by artifact identity, repository manifest digest, both file signatures and SHA-256 content fingerprints checked on every read; changing, deleting or corrupting either file invalidates reuse, even if file size/timestamp is preserved. Cold reads retain checksum/schema checks; warm reads avoid decompression and numeric/list reconstruction without bypassing content verification. Keep public/source read views separate and return isolated values so callers cannot mutate cached science. Coalesce concurrent cold reads with bounded striped locks and release the cache when the service closes.
- Batch RBP bin integration across channels using the existing exact-edge integration primitive. Keep all four backend-computed bin metric arrays because cursor movement must remain local and the frontend must not derive scientific values. Measure their actual JSON cost separately from ordinary window responses.
- Verify response contents, selective decoding, bounded reuse/eviction, corruption and scope rejection, numerical equivalence and projection timings. Synthetic timing is not real-recording playback acceptance.

- [RBP saved-window output may not retain sufficient spectrum for an arbitrary interval] → resolve the storage/projection owner and versioned response shape before UI implementation; retain only the data required for current-window projections and avoid re-reading raw EEG on pointer movement.
- [A dense range drag can create request churn] → debounce requests, cancel or supersede obsolete selections, and gate application on the current selection revision and source window identity.
- [Single-bin RMS may be mistaken for tone amplitude] → label it as equivalent RMS for the selected bin and retain bin bounds in provenance/tooltip.
- [PSD/RBP quality policies may differ] → preserve per-source quality independently and mark unmatched or unavailable metrics instead of making a row appear wholly valid.
- [Frequency-axis boundaries vary with sampling and Welch settings] → use the returned axis and actual supported domain; reject unsupported ranges and test boundary bins.

## Migration Plan

### Measured admission and cancellation overhead repair (2026-11-06)

The 17:18–17:20 experiment measured 49 paired publications: median 452.6 ms,
maximum 1634.9 ms, with maximum window-end lag 1.640 s. All PSD windows were
cached; five RBP windows were new. Each admission opened a median 40 database
connections while rebuilding a 30-row historical montage catalogue. RBP band
formulas took 4–6 ms, while their cancellation checks took 42–98 ms. These
measurements establish infrastructure costs, not a numerical defect or a
general real-time performance guarantee.

- Batch-read snapshot rows and reuse bounded, caller-isolated validated
  catalogues by recording, resolved directory, manifest content digest and full
  snapshot row contents. Re-read those identities on every lookup; changes,
  corruption, new snapshots and source rebinding must not reuse stale science.
- Resolve configuration, metadata, source identity and window plan before the
  service admission gate. Keep attempt/subscription admission and executor
  submission serialized, recheck shutdown after planning, and retain database
  capacity/publication fences and recovery identity validation.
- Give each worker one explicitly closed, autocommit read connection. Every
  cancellation checkpoint reads window state and the exact attempt's state,
  token and expiry in one statement, without materializing a whole window or
  reopening connections. No throttling or local-only cancellation cache;
  cross-process cancellation/lease changes remain visible at the next check.
- Preserve PSD/RBP science, prepared-source identity, public API, topomap and
  waveform ownership. Cold continuous-source preparation and the one-second
  refresh alignment still have a cost; acceptance requires a new comparable
  real-recording experiment after the repair.

No recording/database migration. Add the current-version RBP custom-interval projection and typed response, then bind the Review panel to matched PSD/RBP windows. Deploy backend and desktop together because the custom panel depends on the new backend projection contract. Rollback by disabling/removing the new panel and endpoint consumer; retain the existing five-band numeric panel, PSD/RBP algorithms, saved runs, and topomap behavior. Validate the custom endpoint and full Review workflow before enabling it in the review toolbar.

## Implemented Business Contract And UI Handoff

- Point and range endpoint text inputs show small repeat-button arrows on hover or keyboard focus, reserving a fixed slot so the text does not shift. Click/hold and PreviewKeyDown Up/Down select adjacent actual backend bins through the existing selection setters. Commit typed text first; conversion errors and nonfinite values prevent stepping. Stepping stops at axis limits and excludes candidates that would cross the other range endpoint; missing bins disable buttons. Scientific values and range debounce remain owned by the existing ViewModel/backend path. Palette and table-only styles live in `ReviewCustomFrequencyStyles.xaml`; the input templates and event handlers stay with the panel, keeping each source below 400 lines.
- RBP scientific identity remains `official-rbp-v4`; implementation is `rbp-runtime-v9-private-projection-source`, Definition `2.0.9`, and on-demand policy `rbp-dynamic-window-v4-custom-spectrum`. The five-band formulas, outputs, full-series shapes and topomap fields are unchanged. Only persisted on-demand windows add `frequency_hz` and `[channel, frequency]` `spectral_psd` in `V^2/Hz` from their existing Welch execution. The manifest declares them as backend-only `projection_fields`; ordinary ensure/status reads omit their arrays, units and shapes before decompression. The custom API explicitly reads the internal numeric source. No historical result is rewritten.
- `POST /api/analysis-windows/custom-frequency/bins` returns all ordered channel/bin values for a matched saved-window pair. `POST /api/analysis-windows/custom-frequency/range` projects snapped `low_hz`/`high_hz` from the exact common PSD/RBP grid inside 1–50 Hz against the same pair, using the shared boundary-aware trapezoid policy. The relative-power denominator remains the complete saved five-band RBP total even when selectable support is narrower.
- Point power integrates the midpoint-bounded bin interval using the RBP integration primitive; it is not generally equal to center PSD multiplied by bin width for a varying spectrum. PSD point density still comes directly from the independent PSD result. UI labels must not claim these different source values satisfy an exact pointwise multiplication formula.
- `RecordingReviewViewModel.CustomFrequencyInspection` exposes selection and stable rows; `OpenCustomFrequencyInspectionCommand` activates it. Bin cursor/channel changes make no requests, while range edits use a 180 ms debounce, cancellation and selection/window revision gates. Closing or replacing the window invalidates pending responses.
- The user subsequently requested the initial XAML page. `ReviewCustomFrequencyInspectionPanel` now binds a sortable/scrollable numeric table and `ReviewCustomFrequencyChart`; the chart keeps one SciChart surface, line series and three reusable annotations, displaying one cursor in point mode or two boundaries in range mode. Frequency inputs commit on focus loss or Enter. The standard panel shell retains close/undock behavior. No scientific formulas or API calls enter the view. Automated rendered-control and drag-event checks cover the bindings; real-recording playback/physical pointer acceptance remains open.
- Hover inspection uses SciChart's X-only series snapping with interpolation disabled. SciChart still formats axis hover labels from the raw pointer X, so the standard numeric axis uses a label provider that looks up the nearest X directly in the current data series using the dependency's nearest-index rule. The label and PSD tooltip identify the same sampled point without maintaining a second frequency array. An unavailable curve disables hover; an empty series produces no frequency label. This is separate from the editable selection cursors and does not change the selected range or trigger calculation.
- The chart header retains only its PSD title; channel selection belongs to the left table. Each native frequency annotation owns a blue rounded label above the plot and a white-bordered circular handle at the baseline. Reserve top/right layout space and allow overlay labels outside the plot bounds. Keep close range labels readable by separating their boxes without moving the boundary lines or handles. Labels share the annotation coordinates and visibility; the chart's interaction partial owns mouse capture, hit testing and drag termination instead of SciChart's selection-before-edit lifecycle. Native annotation editing is disabled so there is one drag owner. Boundary labels/handles and the nearest line within 8 DIP take priority over the fill. A blank-plot point press selects immediately; grabbing an existing line/label retains its pointer offset. Fill dragging moves the initial pair with fixed Hz width, clamped to support and snapped to the nearest supported pair (ties lower). On a nonuniform grid only equal-width endpoint pairs are eligible. The linear pair search changes selection coordinates only; no EEG computation runs in the view. `SetRange` assigns both backing fields before notifications, and the existing range debounce/stale guards consume the complete pair. Release/capture loss, mode/grid/model changes, unavailable bins and unload terminate capture. Rendering/diagnostics stay in the main chart file; mouse gesture ownership is isolated by responsibility in `ReviewCustomFrequencyChart.Interaction.cs`, keeping both files below 400 lines. Channel order, time windows, units and API/WebSocket contracts do not change; no persisted data changes.
