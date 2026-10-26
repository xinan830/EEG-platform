## 1. Source inventory and frozen contracts

- [x] 1.1 Audit executable ANT source paths and tests; publish the complete TNK algorithm ledger, excluding presentation-only values and non-algorithm infrastructure.
- [x] 1.2 For every ledger item, freeze formula, channels/roles, reference, units, filters, Welch/FFT settings, frequency bands, windows, cadence, state, warm-up, quality gates, failures, outputs, and tolerances.
  - [x] Reconcile the audited inventory: exclude removed residual-spectrum RBP and include the tested Composite Meditation Index.
  - [x] Compare every candidate's source behavior with focused test assertions, especially missing/invalid inputs, stale-value behavior, and state reset.
    - [x] Review IAPF estimator and lock-state tests, including 4 s Welch, quality gates, candidate cadence, failure handling, and reset.
    - [x] Review relaxation/spatial-distribution/stability and composite aggregation assertions, including cold-start fallback behavior.
    - [x] Review BrainBeat Flat/Fatigue, HAI/aperiodic, FAA, and RBP formula, state, reset, and invalid-input assertions.
  - [x] Resolve module/result boundaries: 12 independent IDs; BrainBeat has explicit window/report modes; aperiodic exponent has per-channel plus pooled output; composite preserves source value with explicit degraded quality evidence.
  - [x] Freeze per-result numerical tolerances and exact filter/Welch/FFT details for the target execution environment.
- [x] 1.3 Compare each TNK definition with same-named platform algorithms and prove existing platform IDs, versions, and results remain unchanged.
- [x] 1.4 Freeze reference environment/library identity and independent analytic fixtures; document any ANT documentation/source/test discrepancy.

## 2. Runtime context replay contract

- [x] 2.1 Model deterministic reset boundary, canonical sample-aligned context, state update order, and target-window output for offline stateful modules.
- [x] 2.2 Add context identity to on-demand planning, cache identity, provenance, cancellation, and resource accounting without adding algorithm-ID branches to shared services.
- [x] 2.3 Declare strategy support and reuse direction independently for every TNK module; return structured unsupported reasons where exact strategy semantics cannot be guaranteed.
- [x] 2.4 Add runtime tests proving target-window equality between full and on-demand execution, context-sensitive non-reuse, cancellation, and bounded result publication.

## 3. Independent scientific references and backend modules

- [x] 3.1 Implement independent reference calculations from frozen ANT formulas without importing production platform calculators.
  - [x] Add independent Welch, paired-epoch FAA, band integration, IAPF/specparam fit, and residual-spectrum reference calculations.
  - [x] Add independent references for HAI, RBP, relaxation, spatial distribution, rhythm stability, composite meditation, BrainBeat/Flat, and fatigue ratios/state primitives.
  - [x] Freeze the deterministic analytic signal recipe, source-file identities, reference environment, scalar/array tolerances, and one exact Welch array checksum.
  - [x] Prove reference code does not import production algorithm/scientific calculators or enter the builtin registry.
- [x] 3.2 Implement stateless TNK window modules with typed configs, manifests, explicit channel roles, units, quality/failure semantics, and compact evidence.
  - [x] Implement unregistered RBP, HAI, relaxation, spatial-distribution, and aperiodic window modules.
  - [x] Implement explicit segment-only FAA and dependency-aware composite result modules.
  - [x] Keep source-window validation, units, sample bounds, provenance, and structured failures inside the module boundary.
- [x] 3.3 Implement stateful TNK modules with deterministic replay, explicit warm-up/locking/smoothing state, and module-owned calculations.
  - [x] Implement resettable IAPF candidate/median lock state and BrainBeat/BrainBeat Flat log-EMA state.
  - [x] Implement per-channel fatigue EMA and ten-frame rhythm-stability history/readiness state.
  - [x] Keep the modules unregistered until catalog and strategy acceptance in chapters 4–5.
- [x] 3.4 Add module-level numeric, boundary, missing-channel, quality rejection, state transition, provenance, and artifact tests for every ledger item.
  - [x] Add focused module and state tests covering all 12 identities, including missing roles, invalid PSD, quality rejection, warm-up, lock, history readiness, segment-only FAA, degraded composite evidence, and sample-bound provenance.
  - [x] Separate module-level contract tests from the complete full/on-demand/combined numeric parity evidence reserved for chapter 5.
- [x] 3.5 Store large spectra/arrays as controlled artifacts with checksum, dimensions, units, environment, source identity, and replayable summaries.
  - [x] Add bounded TNK array descriptor with deterministic array checksum, shapes, units, environment, and source identity.
  - [x] Add the persistence bridge to the existing atomic ArtifactStore; Run JSON receives metadata and artifact reference, not large arrays.

## 4. Registry and WPF integration

- [x] 4.1 Register completed TNK modules through the single built-in composition list and validate unique current identities and catalog contracts.
- [x] 4.2 Add catalog-driven WPF detail modules, parameter serialization, result handlers, previews, and structured quality/failure display for each result shape.
- [x] 4.3 Add a “TNK 脑指标” algorithm-list group linking independent child modules; verify selection submits only the chosen child and does not couple availability or results.
- [x] 4.4 Verify WPF performs no TNK scientific computation and existing algorithm detail modules still resolve unchanged.
- [x] 4.5 Add API/catalog and WPF tests for every TNK identity, strategy declaration, units, output shape, and failure state.
- [x] 4.6 Replace the per-child TNK catalog surface with one `tnk_biofeedback` group entry and a typed canonical role-to-source mapping contract.
- [x] 4.7 Add backend group coordination that dispatches mapped role inputs to independent TNK child modules and aggregates child results without moving scientific formulas into the coordinator.
- [x] 4.8 Remove child-specific TNK channel controls and `secondary_channel`/`tertiary_channel` serialization from the WPF user workflow; show required role mappings and child result sections inside the group detail view.
- [x] 4.9 Add mapping validation, Run snapshot/provenance, API catalog, backend dispatch, and WPF serialization tests; update existing child catalog tests to assert children are internal-only.

## 5. Strategy parity and acceptance

- [x] 5.9 Remove event annotation/report controls and their workbench-only client code. Replace categorical column snapshots with real-time points/curves and RBP stacked trends; preserve a bounded, session-isolated history of returned on-demand windows and explicit gaps. Verify single-window and multi-window rendering before completion.
  - [x] Remove event UI, event commands, activation requests and event-only client contracts; retain the independent recording event API for its existing consumers.
  - [x] Remove TNK column renderers; project single windows at actual recording seconds, accumulate returned windows with duplicate replacement, ordering, null gaps, session resets and a 600-window bound. Desktop algorithm checks: 423 passed.
- [x] 5.10 Make TNK on-demand playback incremental with bounded session-identity checkpoints and per-frame shared spectrum loads; prove sequential targets compute only new frames, random seeks replay from the nearest compatible checkpoint, cold misses preserve full/on-demand parity, and cache identities never cross-contaminate.
  - [x] Add bounded checkpoints keyed by complete `DynamicSessionIdentity`, nearest-prior restore, request-local shared spectra, and per-frame replay diagnostics.
  - [x] Serialize checkpoint restore/replay/save for concurrent requests in the same session using a fixed set of lock stripes; add a concurrent-target regression test.
  - [x] Verify sequential reuse, backward seek, identity miss, spectrum sharing, and cold full/on-demand parity.
  - [x] Validate the authorized real recording read-only against all full dynamic windows and record timing and comparison evidence in `docs/validation/tnk-on-demand-incremental-validation.md`.
  - 2026-10-25: all 146 dynamic windows (37 metrics; 5,402 scalar values) matched full execution exactly, including finite masks, quality/state and child evidence. Sequential targets replayed one frame each. In-process on-demand execution was p50 125.39 ms / p95 147.41 ms / max 161.09 ms. A cold request at frame 90 replayed 91 frames in 12.16 s; the next frame used its checkpoint and completed in 156.56 ms. These timings exclude API queueing and WPF display. O2-REF remains a user-authorized Oz-role proxy, not physical Oz. Focused TNK validation: 48 passed, 1 skipped; OpenSpec strict and diff checks passed. Independent scientific reference validation and live desktop acceptance remain tracked separately under 5.2 and 5.4.

- [x] 5.8 Recreate the ANT workbench layout and chart families with SciChart: shared review-style filtered EEG, analysis tabs including per-channel stacked RBP, compact score/live metrics and playback. Bind current montage/filter settings and returned values; verify real-data rendering and leave manual acceptance explicit. Supersedes the 5.7 visual design.
  - Scope correction under 5.9 removes the event annotation/report controls and categorical-column fallback introduced in this iteration. The evidence below describes the earlier 5.8 implementation.
  - [x] Replace generic cards with original-style waveform / tabbed charts / right controls; retain separate backend child modules.
  - [x] Render real five-channel continuously filtered data and SciChart per-channel four-band RBP; fix read-only progress binding discovered by actual rendering.
  - [x] Finish sidebar interactions, source/filter explanations, regression checks and final visual verification.
  - 2026-10-25: native SciChart five-channel waveform plus eight chart-family tabs were rendered with the saved real Run and continuously filtered samples. The complete AlgorithmDetailView was also rendered with application resource dictionaries; RBP and BrainBeat images were inspected. Sidebar events persist through the existing recording API, and state durations come from a typed backend cumulative timeline fetched once per immutable Run. Navigation rebases playback time, clamps targets, and pauses for slider dragging. Backend focused checks: 34 passed; desktop algorithm checks: 420 passed; WPF render/build, OpenSpec strict validation and diff checks passed. Actual mouse interaction and playback smoothness remain unaccepted under 5.4. Display filters follow selected settings; TNK science retains the frozen 1–30 Hz definition. Original event-period scientific reports are explicitly not implemented and are not replaced by trend averages.

- [x] 5.7 Replace card dashboard with SciChart workbench: shared reusable waveform component, real montage-aware bounded signal source, one selected child chart with zoom/pan/tooltip, and linked recording-time navigation. Remove custom drawing controls, keep compact values and structured reasons, and verify API/UI contracts.
  - Superseded by 5.8 at the user's request: chart-family tabs and the ANT-style sidebar replace the single-child selection layout. The preview now uses canonical continuous filtering with explicit settings.
  - 2026-10-25: implemented reusable Shared/Waveform control and typed bounded signal-preview API, SciChart-only workbench, module/series selection, null gaps and compact current values. Role collection changes are observed; stale waveform requests are cancelled and rejected. Ctrl-click and slider release link recording time; on-demand uses the existing seek coordinator. Backend focused checks: 16 passed; desktop focused checks: 72 passed; OpenSpec strict validation and diff checks passed. Isolated WPF render used the saved real Run (12 children, 11 with data, FAA unavailable) and five-channel waveform, with production SciChart initialization; both waveform and metric plot visibly rendered. Live mouse interactions and complete desktop workflow still require manual acceptance under 5.4.

- [x] 5.6 Consolidate TNK details into one result card per catalog child, with backend numeric values, units, source channels, quality/reasons, and module-owned charts for static/full/on-demand results. Remove duplicate inventories and empty parameter descriptions; test projection, missing values, window identity, and isolated WPF build. Manual visual acceptance remains separate under 5.4.
  - Superseded by 5.7 at the user's request: custom per-child chart cards were removed; backend-only value projection remains.
  - 2026-10-25: added module-owned chart projection and lightweight WPF rendering, actual-time full trends, categorical snapshots, source/metric selection, explicit null gaps, and current-window evidence binding. An isolated render harness loaded the saved real Run: 12 cards, 11 with valid values, FAA unavailable; 37 metric series with 146 points each. WPF build and focused tests pass; manual live workflow acceptance and independent real-data formula validation remain uncompleted.

- [x] 5.1 Compare full, on-demand, and combined execution for each declared-supported strategy using identical recording context, sample bounds, config, and preprocessing.
  - The public TNK group now declares and implements group-level on-demand replay with isolated child state and target-window-only publication. Focused runtime tests passed (`test_tnk_group.py`, `test_tnk_runtime_execution.py`, `test_tnk_strategy_parity.py`: 18 passed).
  - Real recording parity (2026-10-25): a 149.982 s recording was run through all 146 full-dynamic windows; all 37 metric arrays were compared with four on-demand cursor targets using identical mapping/config/preprocessing. Every target matched its full window with max absolute error 0, identical finite masks, state/quality and child evidence. The preview-then-full combined workflow produced an artifact checksum identical to the independent full run; on-demand-to-full reuse is explicitly unsupported for the group, so the combined run independently computed all windows. Detailed report and artifacts are stored outside the repository under `%TEMP%\brain-platform-tnk-validation\report\`.
- [ ] 5.2 Compare output arrays, intermediate values, quality/state transitions, actual bounds, provenance, checksums, and maximum errors against independent references.
  - Independent per-window reference comparison on this real recording remains outstanding. Existing independent-reference checks use deterministic analytic/module fixtures. This run uses the explicitly authorized `Oz -> O2-REF` surrogate and labels it as an O2-input/Oz-role proxy, never as a physical Oz measurement or scientific equivalence. The source was read only; the report records the post-run sample-tree digest, while a before/after tree digest was not persisted by the original run script.
- [x] 5.3 Verify unsupported strategies fail explicitly and no silent substitution, context truncation, or cross-algorithm cache reuse occurs.
- [ ] 5.4 Complete WPF manual acceptance for TNK selection, parameters, result display, unavailable state, dynamic cursor targets, and provenance.
  - Screenshot defect fixed: hide the independent TNK primary-channel selector and derive the Run channel from the canonical Fz role mapping. Isolated WPF build and 48 focused desktop tests passed; OpenSpec strict validation passed. Manual acceptance with the new build remains pending.
  - Result display follow-up: show structured explanations for partial, degraded, insufficient-data, unavailable, and quality-gate states when a child has no explicit failure object; format quality labels consistently.
  - [x] Repair interrupted TNK on-demand chart history: retain completed windows whose cursor waiter became stale, while keeping current numeric values bound only to the latest cursor and preserving gaps for windows that were never computed.
    - Completed observations now publish to a UI-dispatched history path only while their recording, algorithm, and config context remains current; stale cursor waiters do not suppress returned windows. The display projection stores late windows separately from current values/status/provenance, retains actual backend times, bounded history, duplicate replacement, session isolation, and explicit gaps. Added regressions for superseded completion and latest-value preservation; focused desktop tests pass (19).
- [ ] 5.5 Run backend and desktop checks, OpenSpec strict validation, `git diff --check`, and update current algorithm documentation and the task ledger.

- [x] 5.11 Restrict the public TNK group to dynamic analysis: remove the static catalog/UI path, reject new static requests without substitution, preserve historical static Run readability, and add backend/WPF regression coverage.
  - TNK catalog now declares only `dynamic`; a static request is rejected before input loading. The WPF context selects dynamic mode and hides the static action when the catalog omits static. Backend targeted test passed; Release desktop `AlgorithmWorkspaceViewModelTests` passed 18/18.
  - Current workbench iteration: backend 34 passed, desktop algorithm 420 passed, complete detail-page render/build and strict/diff checks passed. This item remains open for final migration checks after outstanding 5.2 and manual 5.4; the investigation notes below are historical.
  - Fixed dynamic-window HTTP serialization: unavailable TNK values remain `NaN` in controlled numeric artifacts but are projected to JSON `null` at the API boundary, preventing `ValueError: Out of range float values are not JSON compliant: nan` from turning a valid ready window into HTTP 500. Added a regression test; focused dynamic/TNK tests passed (18).
  - Fixed the WPF TNK handler falling through to the default “当前模块没有按需窗口展示能力” exception when the backend correctly returned `insufficient_data`; the group now renders the window state/quality and structured failure instead. A focused desktop build was blocked only because two running desktop processes held `BrainPlatform.Desktop.exe`.
  - Fixed on-demand live-value flicker between one-second targets: the right-side cards now retain the latest backend-returned value at its actual timestamp until a newer valid or invalid target point arrives. IAPF retains its declared warm-up/five-second cadence; this is display continuity, not a scientific value update. Also corrected the per-window availability summary so `TNK_UPDATE_NOT_DUE` is mutually exclusive from available/unavailable counts.
  - Reduced TNK chart churn: stable SciChart axes/renderable series are retained for the same panel topology, sequential windows append to their existing data series, non-sequential corrections replace only that series' data, and ordinary updates no longer call `ZoomExtents()` or recreate legends. Manual playback smoothness still requires desktop acceptance under 5.4.
  - Fixed dynamic TNK group frame minimum from 1 s to the spectral service's 4 s Welch minimum; added a regression assertion that no group frame is shorter than 4 s. This fixes the observed `频谱分析窗口至少需要 4 秒` failure path but does not enable group on-demand.
  - Historical real-recording investigation (2026-10-25, superseded by the explicit user-authorized O2 proxy and group parity evidence under 5.1): source is 1000 Hz / 149.982 s and exposes O1/O2 but no physical Oz. A 0–4 s Fz/Pz/F3/F4 spectrum was rejected by the existing 150 µV amplitude gate (observed peak 364.307 µV); a 0–10 s multi-segment spectrum had 0.75 clean ratio. A 4–14 s TNK RBP Fz window computed cleanly. Group on-demand was disabled at that investigation stage; it is now validated/enabled as recorded in 5.1. O2 remains an explicitly labelled proxy, not evidence of physical Oz equivalence.

- [x] 5.12 Prevent IAPF results computed under the pre-rolling schedule from being reused.
  - The group now identifies the 30 s warm-up / 5 s IAPF schedule as dynamic-window policy v2 and implementation v2. A regression test asserts that 4–29 s are structured warm-up, 30 s is the first trailing 30 s evaluation, and subsequent evaluations occur only at five-second cadence. Existing v1 Runs remain readable history but are not current-equivalent results.
- [x] 5.13 Preserve cadence state in full-result chart projection so right-side values skip only explicit `TNK_UPDATE_NOT_DUE` gaps.
  - Full dynamic timeline points now retain backend `schedule_state: not_due`. The compact card explicitly separates the current-window state from the latest valid backend value: scheduled gaps keep that prior value, while a computed unavailable or rejected current point shows its unavailable status and the actual timestamp of the prior valid value. Focused WPF projection/workbench tests passed (18).
- [x] 5.14 Keep the RBP stacked timeline advancing with appended windows, preserve intentional historical panning, and use adaptive time ticks.
  - Stacked-series replacement now advances the visible range only when the previous latest point was still at the visible edge. SciChart chooses approximately ten-second labels at the default range and denser one-second labels after zooming. Unit tests cover follow and manual-history states.
