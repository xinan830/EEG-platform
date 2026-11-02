## Context

Review already owns the recording access session, playback cursor and time
coordinator. Its analysis lifecycle is adapter-based, but the current Review
composition is PSD-specific. The standalone RBP execution path already provides
backend-planned dynamic windows and per-channel quality results.

## Goals / Non-Goals

**Goals:**

- Remove the old PSD composition from Review so the panel boundary is clean.
- Make RBP the first algorithm panel in the cleaned Review composition.
- Reuse Review's recording, montage snapshot, cursor and cancellation boundary.
- Preserve backend ownership of preprocessing, window planning, quality and provenance.
- Keep the panel responsive when the cursor moves or the panel closes.

**Non-Goals:**

- No formula, scientific version, preprocessing or quality-rule change.
- No TNK group integration in this change.
- No deletion of PSD from the backend catalog or the independent algorithm workspace.
- No live acquisition, waveform rendering or full catalog workspace embedding.
- No old executor, old result format or silent compatibility fallback.

## Decisions

1. **Use an explicit RBP review adapter.** The adapter implements the existing
   review-analysis boundary and builds the current RBP request from the Review
   recording registration and compatible analysis montage. A small explicit
   registry/factory selects the registered RBP panel; the coordinator remains
   algorithm-neutral.

2. **Keep one Review clock and one binding.** RBP receives the integer
   recording-relative cursor from `ReviewAnalysisBinding`. The backend chooses
   the half-open sample window. RBP never reads the WPF waveform cache and never
   creates a second playback timer.

3. **Use an RBP-owned presentation model.** The panel parses the typed window
   response into the existing RBP band/channel presentation model, exposing ratio
   values, channel order, actual range, quality and warnings. It does not import
   the algorithm catalog ViewModel or recalculate values.

4. **Validate identity before binding.** The adapter/coordinator verifies the
   recording checksum, algorithm definition/implementation identity, normalized
   configuration digest, window sample range and current scientific version. A
   mismatch clears the foreground result and reports a structured unavailable
   state.

5. **Release only this panel's foreground work.** Closing or replacing RBP
   cancels its request/subscription and detaches its property handlers; shared
   backend work and immutable artifacts remain owned by the backend lifecycle.

6. **Publish data only for a changed result, with stable channel charts.**
   Binding progress, loading and cursor-property notifications must not project
   the previous result again. Within one recording/session and channel layout,
   retain channel history collections and append/replace only the returned
   actual window using `RbpHistoryProjection`. A repeated response is a no-op;
   detach and session changes clear the old presentation. The channel list has
   a bounded viewport and recycles offscreen containers, retaining separate
   channel charts rather than merging channels into one chart.

7. **Show channel histories without an aggregate bar chart.** Per the user's
   layout choice, remove the Review panel's top summary bar chart and percentage
   legend. Use 120-pixel channel charts instead of 220-pixel charts, retaining
   channel labels and axis ticks/values, without axis titles. Remove the title and normal status/window/quality
   headings; expose actual-window and quality details through tooltips. Show
   preparation or failure text when no result exists. Empty parameter/status
   slots in the shared shell collapse without hiding its dock/close controls.
   Backend results, ratio units and the standalone RBP workspace are unchanged.

## Risks / Trade-offs

- **RBP has multi-channel output while PSD is single-channel** → keep channel
  order and per-channel quality in the RBP presenter; do not coerce it into the
  PSD chart contract.
- **RBP window computation may lag the cursor** → show requested cursor and
  returned actual range separately; latest-target and stale-response checks remain
  in the shared Review binding.
- **Review files are already large** → keep adapter and presenter in their
  feature modules; do not add another algorithm branch to the Review ViewModel
  beyond panel selection and lifecycle commands.

## Migration Plan

1. Remove the PSD-only Review commands, panel, settings and adapter registration.
2. Add the RBP adapter and typed presenter without changing the backend API.
3. Register the RBP adapter behind the Review panel selection.
4. Run focused desktop contract tests, build, strict OpenSpec validation and
   manual playback/seek/close acceptance on one recording.
5. If acceptance fails, unregister RBP while leaving the cleaned waveform Review
   and the separate PSD algorithm workspace unchanged; no recording or stored Run
   migration is required.

## Testing Strategy

- Adapter tests for request identity, montage/channel order, scientific version,
  units and unavailable capability.
- Binding tests for latest-target replacement, panel close, recording switch and
  per-channel warning/unavailable projection.
- WPF build and focused Review tests; backend regression tests only if an API or
  scientific implementation file changes.
- Manual acceptance confirms playback remains responsive and RBP follows the
  Review cursor without a second clock or duplicate reader.

## Failed manual acceptance: 2026-10-31

- User observed waveform and RBP freezes after opening the Review RBP panel.
  Desktop session `7d9c7281b52a4fb89ea9283a8cb27643` logged a 4308.9 ms
  dispatcher queue delay at 23:18:01.827; a later backend RBP window completed
  in 201.2 ms (23:18:41). These measurements establish UI blockage and continuing
  backend progress, not the absence of all other performance bottlenecks.
- Confirmed defect: every binding property notification called the presenter,
  which replaced all channel history objects and notified ItemsSource, forcing
  all channel charts to be recreated. A nonvirtualized outer ScrollViewer also
  realized every channel chart even when offscreen.
- Three presenter regression tests failed before the fix and passed afterward.
  Added binding-notification, retained SciChart series, viewport realization and
  recycled-container tests. Manual performance acceptance remains open.
- High-risk file review: `RecordingReviewViewModel.cs` remains above 600 lines;
  this fix adds only the result-property guard and detach reset. Existing split
  registration in `docs/code-size-policy.json` still applies; no new workflow,
  scientific logic, reader or clock is added to that composition file.
- Validation uses an isolated output directory because the user's running WPF
  process locks the normal apphost. It does not stop or replace that process.

## Validation of the presentation fix: 2026-10-31

- The focused desktop suite passed 45 tests with zero failures, including
  binding lifecycle/races, RBP parsing, incremental presentation and virtualized
  chart recycling. The full repository suite was not rerun for this UI fix.
- The isolated Debug WPF build succeeded with zero errors and three existing
  NU1701 dependency compatibility warnings. The acceptance executable is
  `.isolated-build/review-rbp-ui/BrainPlatform.Desktop.exe`.
- Strict validation of `add-rbp-review-panel` and `git diff --check` passed.
- The repository file-size check still fails for two backend files untouched by
  this presentation fix: `on_demand_dynamic.py` is 559 lines versus its reviewed
  limit of 530 (also 559 at HEAD); `spectral_analysis.py` is 562 versus 550
  (553 at HEAD, with earlier uncommitted backend changes). These violations are
  not waived or resolved by this fix.
- Real-recording playback and GPU/UI performance acceptance remain pending;
  passing unit/layout tests does not establish that the reported freeze is gone.
