# Dynamic Metric Replay and Debug Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make dynamic user metrics reset truthfully on replay, show a pending trend before the first complete EEG window, and expose a read-only per-window computation workbench.

**Architecture:** The App owns a monotonically increasing playback epoch and forwards it to the mounted algorithm selector. The selector clears only dynamic result state on a new epoch and keeps its selected settings. Definition-metric Runs persist full spectral evidence for static output and each dynamic point; the app renders that evidence in a modal without calculating EEG or formula values in the browser.

**Tech Stack:** Vue 3 / TypeScript / ECharts / Vitest; FastAPI / Pydantic / NumPy / pytest; existing Run API and OpenSpec.

**Spec:** `openspec/changes/add-user-metric-runs-and-results/design.md`

## Global Constraints

- Scientific calculations remain exclusively in the backend; the browser only formats persisted values.
- `offline-spectral-v3` math and the Viewer display pipeline must not change.
- Dynamic values begin only after a full selected 5/10/20/30-second trailing EEG window exists; never fabricate a zero or interpolated point.
- Keep legacy spectrum, spectrogram and Run APIs compatible.
- Use `apply_patch` for edits and preserve unrelated worktree changes.

---

### Task 1: Persist metric spectral evidence

**Files:**
- Modify: `backend/app/services/definition_metric_runner.py`
- Modify: `backend/app/services/runs.py`
- Test: `backend/tests/test_definition_metric_runs.py`

**Interfaces:**
- Produces `MetricInputResolution.spectral_evidence` containing backend-returned selected-channel frequency/PSD, band powers, contracts, quality and sampling rate.
- Produces `metric.spectral_evidence` for static Runs and `metric.series[*].spectral_evidence`, `inputs`, and `source_quality` for dynamic Runs.

- [ ] **Step 1: Write failing static and dynamic evidence tests**

```python
def test_definition_metric_result_persists_backend_spectral_evidence(client, recording):
    run = create_completed_theta_beta_run(client, recording.id, mode="static")
    evidence = run["result_summary"]["metric"]["spectral_evidence"]
    assert evidence["frequencies_hz"] == pytest.approx([1.0, 1.25])
    assert len(evidence["psd_uV2_per_hz"]) == 117
    assert evidence["band_power"]["theta"] > 0

def test_dynamic_metric_point_persists_its_own_spectral_evidence(client, recording):
    run = create_completed_theta_beta_run(client, recording.id, mode="dynamic", start_s=0, end_s=12)
    point = run["result_summary"]["metric"]["series"][-1]
    assert point["window_start_s"] == pytest.approx(2.0)
    assert point["inputs"]["input_theta"]["feature"] == "theta_power"
    assert len(point["spectral_evidence"]["psd_uV2_per_hz"]) == 117
```

- [ ] **Step 2: Run the focused backend test to verify it fails**

Run: `backend/.venv/Scripts/python.exe -m pytest backend/tests/test_definition_metric_runs.py -q`

Expected: failure because `spectral_evidence` and dynamic point input snapshots are absent.

- [ ] **Step 3: Add a backend-only evidence mapper**

```python
def spectral_evidence_from_payload(payload: dict[str, object], channel: str) -> dict[str, object]:
    return {
        "sfreq_hz": float(payload["sfreq_hz"]),
        "analysis_reference": payload["analysis_reference"],
        "algorithm_version": payload["algorithm_version"],
        "filter_contract": payload["filter_contract"],
        "welch_contract": payload["welch_contract"],
        "frequencies_hz": list(payload["frequencies_hz"]),
        "psd_uV2_per_hz": list(payload["psd"][channel]),
        "band_power": dict(payload["band_power"][channel]),
        "relative_band_power": dict(payload["relative_band_power"][channel]),
        "quality": dict(payload["quality"]),
    }
```

Use this mapper in `resolve_window`, attach it to static metric summaries and
the corresponding dynamic point. Persist source input snapshots for every
clean dynamic point. On a quality-gated dynamic point, persist its gate quality
only; do not invent PSD, band powers or inputs.

- [ ] **Step 4: Run the focused backend test to verify it passes**

Run: `backend/.venv/Scripts/python.exe -m pytest backend/tests/test_definition_metric_runs.py -q`

Expected: all focused tests pass; no raw EEG sample array is returned.

### Task 2: Reset dynamic state for a new replay epoch

**Files:**
- Modify: `frontend/src/App.vue`
- Modify: `frontend/src/components/AlgorithmDisplayWorkspace.vue`
- Modify: `frontend/src/components/AlgorithmDisplayWorkspace.test.ts`

**Interfaces:**
- `App.vue` provides `:playback-epoch="dynamicPlaybackEpoch"`.
- Workspace watches the epoch and clears `runs` plus `lastDynamicRefreshS` without changing selected IDs/channel/window.
- `dynamicSession` emits selected `windowS` and selected algorithm display metadata for a pending chart.

- [ ] **Step 1: Write a failing replay-epoch component test**

```ts
it('clears prior dynamic results but retains selected settings for a new playback epoch', async () => {
  const wrapper = mountWorkspace({ playbackEpoch: 0, dynamicActive: true, playbackPositionS: 22 })
  await enableAndResolveDynamicMetric(wrapper)
  await wrapper.setProps({ playbackEpoch: 1, playbackPositionS: 0 })
  expect(wrapper.emitted('results')?.at(-1)?.[0]).toEqual({})
  expect(wrapper.find('input[type="checkbox"]').element.checked).toBe(true)
})
```

- [ ] **Step 2: Run the focused component test to verify it fails**

Run: `frontend/node_modules/.bin/vitest run src/components/AlgorithmDisplayWorkspace.test.ts`

Expected: failure because the component has no playback epoch input or reset behavior.

- [ ] **Step 3: Implement the epoch reset**

```ts
const dynamicPlaybackEpoch = ref(0)
async function replay() {
  if (dynamicAlgorithmSession.value) {
    algorithmDisplayResults.value = {}
    dynamicPlaybackEpoch.value += 1
  }
  // existing waveform replay continues unchanged
}

watch(() => props.playbackEpoch, () => {
  runs.value = {}
  lastDynamicRefreshS.value = null
  emit('results', {})
})
```

Do not disable the active dynamic session, clear checkbox state, or call the
backend until the regular whole-second playback watcher reaches the selected
window duration.

- [ ] **Step 4: Run the focused component test to verify it passes**

Run: `frontend/node_modules/.bin/vitest run src/components/AlgorithmDisplayWorkspace.test.ts`

Expected: the old dynamic point is absent after replay while configuration remains selected.

### Task 3: Render an immediate truthful dynamic pending trend

**Files:**
- Modify: `frontend/src/components/DefinitionMetricTrendChart.vue`
- Modify: `frontend/src/App.vue`
- Test: `frontend/src/components/DefinitionMetricTrendChart.test.ts`

**Interfaces:**
- `DefinitionMetricTrendChart` accepts either a completed `DynamicMetric` or `pending` metadata `{ label, unit, channel, windowS }`.
- A pending panel has an empty ECharts data array, X range `0..windowS`, and explicit pending copy.

- [ ] **Step 1: Write a failing pending-chart test**

```ts
it('shows an empty 0–10 second pending chart without fabricating a metric value', async () => {
  const wrapper = mount(DefinitionMetricTrendChart, { props: { result: null, pending: { label: 'Theta/Beta 比值', unit: 'dimensionless', channel: 'F3', windowS: 10 } } })
  expect(wrapper.text()).toContain('等待第一个完整窗口：0.000–10.000 s')
  expect(chartSetOption).toHaveBeenCalledWith(expect.objectContaining({ series: [expect.objectContaining({ data: [] })] }), true)
})
```

- [ ] **Step 2: Run the focused chart test to verify it fails**

Run: `frontend/node_modules/.bin/vitest run src/components/DefinitionMetricTrendChart.test.ts`

Expected: failure because the chart currently requires a non-null result.

- [ ] **Step 3: Implement pending rendering and session metadata**

```ts
const active = computed(() => props.result ?? {
  output: { label: props.pending!.label, unit: props.pending!.unit },
  channel: props.pending!.channel,
  dynamic_contract: { window_s: props.pending!.windowS, step_s: 1 },
  series: [],
})
```

Keep `series: []`, `connectNulls: false`, and no current metric value while
pending. `App.vue` renders one pending chart for each selected algorithm when
the replay epoch/session has no completed dynamic result.

- [ ] **Step 4: Run the focused chart test to verify it passes**

Run: `frontend/node_modules/.bin/vitest run src/components/DefinitionMetricTrendChart.test.ts`

Expected: panel appears immediately, displays no metric output, and labels the first exact real window.

### Task 4: Implement the read-only algorithm debug workbench

**Files:**
- Create: `frontend/src/components/AlgorithmMetricDebugDialog.vue`
- Modify: `frontend/src/api/runs.ts`
- Modify: `frontend/src/components/AlgorithmDisplayWorkspace.vue`
- Modify: `frontend/src/App.vue`
- Test: `frontend/src/components/AlgorithmMetricDebugDialog.test.ts`

**Interfaces:**
- `AnalysisRunResponse` exposes optional persisted run provenance fields and `result_summary`.
- Dialog props: `{ run: AnalysisRunResponse; definitionName: string }`.
- Dynamic debug dialog chooses a persisted `series` point by `window_end_s`, latest clean or latest available point by default.

- [ ] **Step 1: Write a failing dialog test using only persisted Run data**

```ts
it('renders selected dynamic-window evidence without calling an EEG calculation API', async () => {
  const wrapper = mount(AlgorithmMetricDebugDialog, { props: { run: dynamicRunWithEvidence, definitionName: 'Theta/Beta 比值' } })
  expect(wrapper.text()).toContain('实际分析区间：12.000–22.000 s')
  expect(wrapper.text()).toContain('Theta 功率')
  expect(wrapper.text()).toContain('4 s Hann')
  expect(wrapper.text()).toContain('117 个频率点')
})
```

- [ ] **Step 2: Run the focused dialog test to verify it fails**

Run: `frontend/node_modules/.bin/vitest run src/components/AlgorithmMetricDebugDialog.test.ts`

Expected: failure because the dialog does not exist.

- [ ] **Step 3: Build the dialog and wire access buttons**

```vue
<button type="button" class="algorithm-debug-button" @click="openDebug(item)">算法调试台</button>
<AlgorithmMetricDebugDialog
  v-if="debugRun"
  :run="debugRun"
  :definition-name="debugDefinitionName"
  @close="debugRun = null"
/>
```

Render sections in this order: selected window and mode; Run/definition
identity; channel/reference/sampling; filtering/Welch; quality; resolved
input values; computed output; four band powers/RBP; collapsible backend
frequency/linear-PSD table. For dynamic evidence, point selection changes only
which persisted point is displayed. No handler may call a calculation endpoint.

- [ ] **Step 4: Run the focused dialog test to verify it passes**

Run: `frontend/node_modules/.bin/vitest run src/components/AlgorithmMetricDebugDialog.test.ts`

Expected: it presents the exact persisted selected point and makes no API calls.

### Task 5: Verify and document the completed behavior

**Files:**
- Modify: `openspec/changes/add-user-metric-runs-and-results/tasks.md`
- Test: `backend/tests/test_definition_metric_runs.py`
- Test: `frontend/src/components/AlgorithmDisplayWorkspace.test.ts`
- Test: `frontend/src/components/DefinitionMetricTrendChart.test.ts`
- Test: `frontend/src/components/AlgorithmMetricDebugDialog.test.ts`

- [ ] **Step 1: Mark completed implementation items in the active OpenSpec task list**

```markdown
- [x] Reset dynamic metrics at replay while retaining selected session settings.
- [x] Render a truthful pending dynamic chart before the first complete window.
- [x] Persist and render read-only per-window algorithm evidence.
```

- [ ] **Step 2: Run all backend tests**

Run: `backend/.venv/Scripts/python.exe -m pytest -q`

Expected: all tests pass.

- [ ] **Step 3: Run all frontend tests and production build**

Run: `frontend/node_modules/.bin/vitest run; D:/nodejs/npm.cmd run build`

Expected: all tests pass and Vue type checking plus Vite production build succeed.

- [ ] **Step 4: Validate the active OpenSpec change and check the diff**

Run: `openspec validate add-user-metric-runs-and-results --strict --no-interactive; git diff --check`

Expected: strict OpenSpec validation succeeds and diff check reports no whitespace errors.

- [ ] **Step 5: Commit the focused change**

```bash
git add backend frontend openspec/changes/add-user-metric-runs-and-results docs/superpowers/plans/2026-09-15-dynamic-metric-replay-and-debug-workbench.md
git commit -m "Improve dynamic metric replay and debugging"
```
