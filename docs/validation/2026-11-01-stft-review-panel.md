# STFT recording Review panel verification — 2026-11-01

## Scope and integration points

STFT is selectable beside RBP in recording Review through the algorithm menu and the `STFT 时频` toolbar button. Only one algorithm panel is active. This integration uses the existing registered recording, montage snapshot and workspace playback cursor; it creates neither a recording reader nor a playback clock.

| Boundary | Implementation |
| --- | --- |
| Desktop composition | `MainWindow.xaml.cs` constructs RBP/STFT `ReviewAnalysisCoordinator` instances with the shared `ReviewWindowAnalysisAdapter`. |
| Requests and identity | `Modules/Review/Analysis/ReviewWindowAnalysisAdapter.cs` routes only its declared algorithm and preserves scientific configuration, backend identity, axes, units, quality, evidence and provenance. Full Run and legacy-window methods remain unsupported. |
| STFT configuration | `ReviewStftParameters.cs` uses the catalog scientific version/default window/refresh step, selected available montage output and explicit 1–50 Hz analysis filters, independent of waveform display filters. |
| Panel lifecycle | `RecordingReviewViewModel.Analysis.cs` serializes preparation, cancels old requests and rejects obsolete configuration revisions. `AnalysisBinding.cs` owns presentation subscriptions and clears an old failure message after a successful subsequent window. |
| Result projection | `StftPreview.FromReviewWindow` shares standalone STFT validation: ready/clean time-frequency result, matrix dimensions, finite cells, monotonic uniform axes, units and bounded-preprocessing provenance. Relative centers gain the actual window start once. |
| UI | `StftReviewAnalysisPanel` reuses `StftHeatmap` with `RetainHistory=True`. Validated windows append only later time columns, retaining previously drawn estimates at overlapping times. Channel selection shares the header row with dock/close actions; units and concise failure reasons remain visible without a result-evidence expander. Review time formatting is inherited. |

No backend formula, HTTP schema, storage migration, scientific quality threshold or numerical version is changed by this panel integration. It uses the previously implemented bounded-window STFT execution; it does not reintroduce whole-record preprocessing. STFT retains its existing strict quality contract, independently of RBP's artifact warning policy.

The repository already had uncommitted standalone RBP/STFT and RBP Review work. Those changes are retained; this report does not represent all working-tree changes as new STFT-panel work. No commit or push was performed.

## Automated evidence

Environment: Windows; .NET SDK `11.0.100-rc.3.26522.148`, target `net10.0-windows7.0`. Build/test output: `.isolated-build/stft-review/` (generated artifacts, not source).

- Initial focused desktop tests: **73 passed, 0 failed, 0 skipped**. Coverage includes shared adapter routing, RBP regression, STFT units and absolute time, malformed/rejected result clearing and evidence, channel rebind at a paused cursor, close/reopen, switch/close during pending catalog/window work, first-load failure recovery, current-window chart replacement and inherited time formatting. The initial montage test incorrectly accepted mappings for all available outputs; the correction below replaces that assertion.
- Isolated desktop build: **0 errors, 3 warnings**, including WPF/BAML compilation. Existing NU1701 warnings concern OpenTK, OpenTK.GLWpfControl and SkiaSharp.Views.WPF compatibility. Test compilation also reports the existing xUnit2013 warning in `OfflineAnalysisSelectionTests`.
- `openspec validate add-stft-review-panel --strict`: passed.
- `git -c core.safecrlf=false diff --check`: passed. New source/spec/test files were also checked for trailing whitespace.
- Explicit desktop size assessment across **49** modified/new C#/XAML source/test files: **0 failures** after registering responsibilities in `docs/code-size-policy.json`.
- Full repository size script still fails for unchanged `backend/app/services/on_demand_dynamic.py`: **559 lines versus registered 530**. This integration does not change that service or relax its policy.

Focused test command:

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj `
  -o .isolated-build/stft-review --no-restore `
  --filter 'FullyQualifiedName~Channel_change_clears_image|FullyQualifiedName~Failed_initial_load_status|FullyQualifiedName~Switching_or_closing_rejects|FullyQualifiedName~StftReview|FullyQualifiedName~RbpReview|FullyQualifiedName~ReviewOnDemand|FullyQualifiedName~ReviewAnalysisBinding|FullyQualifiedName~AnalysisPanelComposition|FullyQualifiedName~RecordingReviewAnalysisPresentation|FullyQualifiedName~StftPreview|FullyQualifiedName~StftHeatmap|FullyQualifiedName~StftOnDemand|FullyQualifiedName~RbpOnDemand|FullyQualifiedName~AnalysisMontageSelectionTests' `
  --logger 'console;verbosity=minimal'
dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj `
  -o .isolated-build/stft-review --no-restore --verbosity quiet
```

An earlier broader `Review|Stft|Rbp` run produced **336 passed and 2 failed** before the final three tests were added. The two failures reproduced separately (the false playback case passed):

1. `ReviewWaveformRefreshTests.Reapplying_the_same_frame_and_geometry_does_not_rebuild_series`: expects one renderable series but observes two. Test and `RecordingReviewSciChartCanvas.cs` are unchanged relative to HEAD; no algorithm panel is constructed.
2. `RecordingReviewViewModelTests.Dragging_exclusively_owns_position_then_restores_previous_playback_state(wasPlaying: True)`: expects paused playback during drag. The unchanged `BeginTimelineDrag` sets the drag flag without pausing playback; this test creates no algorithm coordinator.

These failures are outside the STFT integration path and were not modified. No separate clean HEAD checkout was tested, so the full suite is **not** reported green.

## Real-recording mapping failure correction

The user's screenshot reports `ANALYSIS_MONTAGE_MAPPING_MISMATCH` with `Fp1-REF` selected. Source inspection confirms that `ReviewStftParameters.BuildConfig` requested one channel but serialized all available montage outputs. The on-demand backend requires exact equality between requested inputs and mapped output ids, with registered roles preserved. No matching backend log entry was found; the diagnosis is based on the screenshot and the two source boundaries.

The request builder now filters available mappings to the selected channel. The full ordered selector list, registered role/unit and montage id/revision/fingerprint are preserved. Backend validation, scientific versions, cache policy and RBP's multi-input request construction are unchanged.

Before the fix, both selected-only regression tests failed because two mappings were serialized. After the fix, the focused suite including standalone montage request tests passes: **78 passed, 0 failed, 0 skipped**. It covers first selection of `Fp1-REF`, switching to `F7-REF`, unavailable-output exclusion, role-preserving montage replacement and null mappings without a montage. Both changed source/test files remain below 400 lines (69/177). Strict OpenSpec validation passes. The rebuilt desktop still requires real-recording GUI retest; this correction does not establish performance acceptance.

## Accumulating time display

At the user's request, Review now enables the existing `StftHeatmap` history path. The presenter validates each new window before publishing it without an intermediate null value, so successful updates retain existing chart segments. Only columns later than the rendered time are appended; overlapping times keep the first displayed backend estimate. Forward jumps leave gaps, backward windows start a new history, and configuration resets, closure and invalid/rejected results clear it. This changes presentation only and does not precompute or read the full recording.

Focused tests now pass **79/79**, including actual presenter-to-panel chart binding, overlap/repeated-window deduplication, backward reset, null reset, forward gaps and rejection clearing, alongside standalone STFT and existing RBP/lifecycle checks. Isolated WPF rebuild succeeds with **0 errors and 3 existing NU1701 warnings**. Strict OpenSpec and diff checks pass. The four changed source/test files have 37, 41, 197 and 88 lines. The first gap test run failed because the test control had not been measured/arranged; initializing its visual tree corrected the fixture before the successful run.

History accumulates only returned valid data and grows in memory/segment count with visited recording time. Interactive appearance and long-duration performance remain unverified; the automated checks are not evidence of GUI responsiveness.

## Size review

The main Review view model is **643 lines**, within its revised registered maximum but above the 600-line high-risk threshold. Algorithm preparation and binding were extracted into cohesive `Analysis` and `AnalysisBinding` partials. The remaining event commands and waveform notification/scheduling responsibilities should be separated before expanding those workflows. Moving them during STFT integration would increase playback/event risk without improving the panel boundary. `MainWindow.xaml.cs` (527), Review XAML (484), and existing affected test files above 400 have explicit responsibility reviews. No new STFT-specific source file exceeds 400 lines.

## Manual acceptance — pending

Use the new desktop build, not an already-running older executable. Keep the normal backend running. Open a known local recording in **数据回放**, then select **STFT 时频** in the toolbar or the algorithm menu.

1. Play beyond the required STFT input window. Confirm that the heatmap appears with frequency ticks and `dB re 1 µV²/Hz`. Early insufficient input must show a reason rather than fabricated values.
2. Pause, seek forward and backward, then resume. Confirm that STFT follows the workspace cursor and the waveform neither resets nor gains another playback control. Switch recording-time/clock-time formatting and check STFT axis labels.
3. While paused, select a different STFT channel. Confirm that the old image clears, the selected channel remains selected, and the replacement uses the same cursor. Change the viewing montage and inspect the returned configuration/evidence for the new snapshot.
4. Switch RBP ↔ STFT while loading; close and reopen the panel. Confirm that late results from the previous selection never appear in the active panel.
5. Inspect a known rejected or unavailable window. Confirm the concise backend reason is visible without a `结果证据` section, and no zero-filled image or previous image is presented as the failed result.
6. Dock/undock and resize the panel. Confirm that successive valid windows extend the heatmap to the right, repeated overlapping windows do not duplicate columns and a forward jump leaves unvisited times unfilled. Seek backward and confirm a new history begins. Compare waveform playback with STFT closed/open over a longer recording and check responsiveness and memory: displayed history grows with visited valid time rather than preprocessing the whole recording.

Real-recording GUI behavior, binding diagnostics during an interactive application run, docking acceptance, end-to-end latency and long-duration CPU/memory performance have **not** been verified by this automated run. No real-time acquisition or backend regression suite was run for this desktop-only integration.

## Compact presentation update

The STFT channel selector occupies the shared panel header beside dock and close actions. The result-evidence expander is absent; failure states retain concise reason text. The unit remains beside the heatmap color scale. Header alignment and no-expander presentation are covered by a focused WPF visual-tree test. The focused STFT Review tests pass **13/13**; isolated WPF build succeeds with **0 errors and 3 existing NU1701 warnings**. Strict OpenSpec validation and `git diff --check` pass. Manual visual acceptance remains pending.

The shared STFT heatmap now shows tick values without axis titles, reduces the vertical axis rail from 78 to 32 WPF units and limits the bottom axis to 42 pixels. A chart test locks these layout constraints. They apply to both the standalone STFT workspace and the Review panel. The combined focused heatmap and Review chart tests pass **6/6**; isolated WPF build succeeds with **0 errors and 3 existing NU1701 warnings**. Strict OpenSpec validation and `git diff --check` pass.
