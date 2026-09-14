# User Metric Runs and Results Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute a saved private scalar metric against real EEG and present its traceable value, unit, quality and eligible input comparison.

**Architecture:** Add `definition_metric` to the existing persistent AnalysisRun queue. Backend code resolves saved curated features through `offline-spectral-v3`, sends typed Scalars to the existing closed graph executor, and persists the output. Vue starts and polls normal Runs, then only displays returned data.

**Tech Stack:** FastAPI, Pydantic, SQLite, NumPy, existing Definition Engine, Vue 3, TypeScript, Vitest, ECharts.

**Spec:** `docs/superpowers/specs/2026-09-14-user-metric-runs-and-results-design.md`; `openspec/changes/add-user-metric-runs-and-results/`

## Global Constraints

- Browser code must not calculate PSD, band power or formula outputs.
- Static user metrics use `offline-spectral-v3`, one existing channel and an absolute range of at least four seconds.
- Null output with structured reason is required for unavailable/gated input; zero is forbidden.
- A scalar result is a number card, never a fabricated one-point trend chart.
- A comparison chart is allowed only when every rendered input has the same backend-declared unit.

### Task 1: Resolve curated feature values

**Files:** Create `backend/app/models/definition_metric_run.py`, create `backend/app/services/definition_metric_runner.py`, test `backend/tests/test_definition_metric_runner.py`.

- [ ] Write `test_resolves_absolute_theta_and_beta_for_requested_channel`: a saved draft with `theta_power` and `beta_power` must resolve both inputs as `Unit.UV2` and retain the selected channel.
- [ ] Run `backend/.venv/Scripts/python.exe -m pytest backend/tests/test_definition_metric_runner.py -q`; confirm it fails because `DefinitionMetricRunner` is absent.
- [ ] Implement `DefinitionMetricConfig`, a closed feature mapping for four absolute bands and four relative bands, and `resolve_inputs(recording, draft, config)`. It calls `RecordingService.load_spectrum(recording, start_s, end_s-start_s, [channel])`; absolute values come from `band_power[channel][band]` as `Unit.UV2`, relatives from `relative_band_power[channel][band]` as `Unit.RATIO`.
- [ ] Write and pass missing-channel and unsupported-feature tests using `DefinitionEngineError` with structured codes.
- [ ] Commit resolver files with message `Add definition metric feature resolver`.

### Task 2: Persist and execute normal metric runs

**Files:** Modify `backend/app/models/run.py`, `backend/app/services/runs.py`, `backend/app/services/run_queue.py`; test `backend/tests/test_definition_metric_runs.py`.

- [ ] Write `test_definition_metric_run_persists_ratio_and_provenance`: POST `/api/runs` with `analysis_type: definition_metric`, a definition/version, `channel: F3`, and `time: 0–30`; wait for terminal state and assert completed scalar output has `dimensionless` unit, actual range, input snapshot and quality.
- [ ] Run that test; confirm it fails because `definition_metric` is not accepted by `RunCreateRequest`.
- [ ] Add the literal analysis type, validate saved definition ID/version and config in `_resolve_request`, calculate canonical feature snapshot/digest, and execute graph outputs in `_execute`. Write `metric_value` plus input values as compressed NPZ arrays and put serializable output/input/quality/channel/chart metadata in `result_summary.metric`.
- [ ] Add and pass tests for unknown version, unavailable feature, missing channel, quality gate with null output, and equal input/config/range cache identity.
- [ ] Commit backend run files with message `Run saved user metrics against EEG`.

### Task 3: Build unit-safe client display primitives

**Files:** Create `frontend/src/api/runs.ts`, `frontend/src/components/DefinitionMetricResultCard.vue`, `frontend/src/components/MetricInputComparisonChart.vue`; tests `frontend/src/api/runs.test.ts`, `frontend/src/components/DefinitionMetricResultCard.test.ts`, `frontend/src/components/MetricInputComparisonChart.test.ts`.

- [ ] Write an API test that `createDefinitionMetricRun` posts `definition_metric`, definition/version, recording, channel and absolute time to `/api/runs`; verify it fails before implementation.
- [ ] Implement `createDefinitionMetricRun` and `waitForTerminalRun`; polling uses GET `/api/runs/{id}` and stops only on existing terminal statuses.
- [ ] Write a card test asserting scalar name/value/unit/channel/range/quality appear and a chart test asserting incompatible units render no chart; verify both fail before components exist.
- [ ] Implement card-only scalar presentation. Implement ECharts comparison with input names on X and the one shared persisted unit on Y; return no chart for no inputs, null input values or mixed units.
- [ ] Run focused Vitest files to green and commit with message `Show unit-safe metric result cards`.

### Task 4: Connect the ordinary workflow and full Results view

**Files:** Modify `frontend/src/components/AlgorithmDefinitionWorkbench.vue`, `frontend/src/components/ResultsDrawer.vue`, `frontend/src/App.vue`; tests `frontend/src/components/AlgorithmDefinitionWorkbench.test.ts`, `frontend/src/components/ResultsDrawer.test.ts`.

- [ ] Write a workbench test that selecting a private saved definition exposes channel plus `运行此算法`, and clicking it posts the active range and selected version; verify red.
- [ ] Add a normal-mode run section only for private user definitions. On terminal result render `DefinitionMetricResultCard`; do not expose developer graph fields or recompute values.
- [ ] Write a Results Drawer test for `definition_metric` summary rendering, including actual range, quality, output unit and export link; verify red then implement it.
- [ ] Run the two component tests to green and commit with message `Run user metrics from the algorithm library`.

### Task 5: Verify, archive and push

**Files:** Modify `openspec/changes/add-user-metric-runs-and-results/tasks.md`, `openspec/roadmap.md`.

- [ ] Run `backend/.venv/Scripts/python.exe -m pytest -q`, `D:/nodejs/npm.cmd test -- --run`, `D:/nodejs/npm.cmd run build`, `openspec validate add-user-metric-runs-and-results --strict --no-interactive`, and `git diff --check`.
- [ ] Mark completed tasks, archive the OpenSpec change, run `openspec validate --all --strict --no-interactive`, update roadmap status, commit and push.
