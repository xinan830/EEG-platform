# Unified Analysis Provenance Debug Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Return one backend-authored, immutable analysis-provenance record with every Run and render it through one shared Chinese read-only debug panel.

**Architecture:** The backend derives an additive `analysis_provenance` response projection from the persisted `AnalysisRun` plus the completed result summary. It contains only evidence already produced by the backend, including an explicit Welch step. Vue consumes that projection without recalculating science, while extension renderers display metric inputs/outputs and spectral evidence supplied by the API.

**Tech Stack:** FastAPI, Pydantic, SQLite-backed `AnalysisRun`, NumPy/SciPy spectral pipeline, Vue 3, TypeScript, Vitest.

**Spec:** `docs/superpowers/specs/2026-09-15-analysis-provenance-debug-design.md`

## Global Constraints

- Do not change `offline-spectral-v3` mathematics, frequency boundaries, units, sampling behavior, channel order, or static/dynamic time semantics.
- The frontend may format values and compute UI-only presentation state, but must not recompute EEG, PSD, band power, RBP, Welch step, or algorithm values.
- `analysis_provenance` is additive to existing Run and result API payloads; old fields and routes remain available.
- The backend must declare `welch.step_s`; the frontend must display that supplied value and never derive it from segment length and overlap.
- Missing, gate-failed, or non-finite evidence remains `null` with its backend quality/error reason; it is never displayed as zero.
- Keep the existing local single-user SQLite architecture and do not add tables, plugin execution, or clinical conclusions.

---

## File Structure

- Create `backend/app/services/analysis_provenance.py`: one pure serializer for public Run projections and the `analysis-provenance-v1` schema.
- Modify `backend/app/eeg_core/analysis_contract.py`: declare the frozen Welch step as a backend contract field.
- Modify `backend/app/services/spectral_analysis.py`: expose the explicit contract step on PSD responses without changing PSD calculation.
- Modify `backend/app/api/runs.py` and `backend/app/services/results.py`: return the common projection for Run resources and result views.
- Create `backend/tests/test_analysis_provenance.py`: API and serializer regression tests for static, dynamic, failed, and result-view behavior.
- Create `frontend/src/types/analysisProvenance.ts`: API types for base provenance and typed extensions.
- Create `frontend/src/components/AnalysisProvenancePanel.vue`: reusable Chinese base evidence panel.
- Create `frontend/src/components/AnalysisProvenanceExtensions.vue`: registry for the initial backend extension kinds.
- Modify `frontend/src/api/runs.ts`: add the optional `analysis_provenance` field to `AnalysisRunResponse`.
- Modify `frontend/src/components/AlgorithmMetricDebugDialog.vue`: replace duplicated base fields with the shared panel and extensions.
- Create `frontend/src/components/AnalysisProvenancePanel.test.ts`: component behavior tests.
- Modify `frontend/src/components/AlgorithmMetricDebugDialog.test.ts`: ensure the dialog uses backend provenance and does not derive Welch step.

### Task 1: Declare explicit backend Welch-step evidence

**Files:**
- Modify: `backend/app/eeg_core/analysis_contract.py`
- Modify: `backend/app/services/spectral_analysis.py`
- Test: `backend/tests/test_spectral_analysis.py`

**Interfaces:**
- Produces `ANALYSIS_CONTRACT["welch_step_s"]: float`.
- Produces PSD payload `welch_contract["welch_step_s"]: float`.

- [ ] **Step 1: Write failing contract assertions**

```python
def test_frozen_spectrum_exposes_declared_welch_step(recording_service, recording):
    payload = recording_service.load_spectrum(recording, 0.0, 10.0, ["F3"])
    assert payload["welch_contract"]["welch_step_s"] == 2.0
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run: `pytest backend/tests/test_spectral_analysis.py -k welch_step -v`

Expected: FAIL because the payload has no `welch_step_s` key.

- [ ] **Step 3: Add the frozen contract field and expose it**

```python
# analysis_contract.py
"welch_step_s": 2.0,

# spectral_analysis.py: the existing welch_contract tuple
"welch_segment_s", "welch_segment_overlap", "welch_step_s", "welch_window", "welch_scaling",
```

Do not alter the estimator or use this field to change the overlap calculation; it documents the already frozen 4 s / 50% contract.

- [ ] **Step 4: Run focused and spectral regression tests**

Run: `pytest backend/tests/test_spectral_analysis.py -v`

Expected: PASS with existing golden PSD and band-power values unchanged.

- [ ] **Step 5: Commit the self-contained contract evidence change**

```powershell
git add backend/app/eeg_core/analysis_contract.py backend/app/services/spectral_analysis.py backend/tests/test_spectral_analysis.py
git commit -m "feat: expose frozen Welch step in spectral evidence"
```

### Task 2: Build the common backend provenance projection

**Files:**
- Create: `backend/app/services/analysis_provenance.py`
- Test: `backend/tests/test_analysis_provenance.py`

**Interfaces:**
- Produces `build_analysis_provenance(run: AnalysisRun) -> dict[str, object]`.
- Produces `serialize_analysis_run(run: AnalysisRun) -> dict[str, object]` whose `analysis_provenance` key is always present.

- [ ] **Step 1: Write failing unit tests for completed static and dynamic metric runs**

```python
def test_static_metric_run_has_backend_authored_provenance(completed_static_run):
    provenance = build_analysis_provenance(completed_static_run)
    assert provenance["contract_version"] == "analysis-provenance-v1"
    assert provenance["actual_range"] == {"start_s": 0.0, "end_s": 10.0}
    assert provenance["welch"]["step_s"] == 2.0
    assert provenance["extensions"][0]["kind"] == "spectral_band_power"

def test_dynamic_metric_run_uses_latest_completed_point_evidence(completed_dynamic_run):
    provenance = build_analysis_provenance(completed_dynamic_run)
    assert provenance["mode"] == "dynamic"
    assert provenance["actual_range"] == {"start_s": 5.0, "end_s": 15.0}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run: `pytest backend/tests/test_analysis_provenance.py -v`

Expected: FAIL because `analysis_provenance` module does not exist.

- [ ] **Step 3: Implement a pure projection with no numerical recomputation**

```python
def serialize_analysis_run(run: AnalysisRun) -> dict[str, object]:
    payload = run.model_dump(mode="json")
    payload["analysis_provenance"] = build_analysis_provenance(run)
    return payload
```

`build_analysis_provenance` must select the latest completed dynamic series point when one exists, otherwise the completed static evidence. It must copy (not integrate or recompute) `spectral_evidence` values into these base fields: contract/scientific/implementation versions, config hash, mode, requested/actual range, channel mapping, reference, `sfreq_hz`, filter contract, Welch contract with `step_s`, frequency span/count, and quality. It must create only these initial extensions:

```python
{"kind": "spectral_band_power", "data": {"band_power": evidence["band_power"], "relative_band_power": evidence["relative_band_power"], "unit": "uV^2"}}
{"kind": "metric_inputs_output", "data": {"inputs": point_or_summary["inputs"], "output": point_or_summary["output"], "unit": point_or_summary["unit"]}}
```

If evidence is absent, `actual_range`, `welch`, `frequency`, `quality`, and `extensions` must be `None` or an empty list as appropriate; preserve the Run status and structured error rather than inventing values.

- [ ] **Step 4: Add a failed-run preservation test and pass all focused tests**

```python
def test_failed_run_provenance_keeps_missing_evidence_null(failed_run):
    provenance = build_analysis_provenance(failed_run)
    assert provenance["quality"] is None
    assert provenance["extensions"] == []
    assert provenance["status"] == "failed"
```

Run: `pytest backend/tests/test_analysis_provenance.py -v`

Expected: PASS.

- [ ] **Step 5: Commit the serializer and its tests**

```powershell
git add backend/app/services/analysis_provenance.py backend/tests/test_analysis_provenance.py
git commit -m "feat: add immutable analysis provenance projection"
```

### Task 3: Return provenance consistently from Run and result APIs

**Files:**
- Modify: `backend/app/api/runs.py`
- Modify: `backend/app/services/results.py`
- Test: `backend/tests/test_run_api.py`
- Test: `backend/tests/test_results.py`

**Interfaces:**
- All successful Run create/list/get/cancel/retry response items use `serialize_analysis_run`.
- `ResultService.view(run_id)["run"]` uses the same projection.

- [ ] **Step 1: Write failing API compatibility tests**

```python
def test_get_run_includes_additive_analysis_provenance(client, completed_run):
    body = client.get(f"/api/runs/{completed_run.run_id}").json()
    assert body["run_id"] == completed_run.run_id
    assert body["analysis_provenance"]["contract_version"] == "analysis-provenance-v1"

def test_result_view_uses_same_run_provenance(result_service, completed_run):
    assert result_service.view(completed_run.run_id)["run"]["analysis_provenance"]["config_sha256"] == completed_run.config_sha256
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run: `pytest backend/tests/test_run_api.py backend/tests/test_results.py -k provenance -v`

Expected: FAIL because API payloads call `model_dump` directly.

- [ ] **Step 3: Replace direct Run serialization only**

```python
from app.services.analysis_provenance import serialize_analysis_run

return serialize_analysis_run(run)
```

Apply the helper to every successful Run object response in `runs.py`; do not modify artifact response serialization. In `ResultService.view`, use `serialize_analysis_run(run)`. Keep status codes, error code paths, field names, and existing top-level Run fields unchanged.

- [ ] **Step 4: Run API and result regressions**

Run: `pytest backend/tests/test_run_api.py backend/tests/test_results.py -v`

Expected: PASS; old clients still see all pre-existing fields and new clients see the additive provenance field.

- [ ] **Step 5: Commit API integration**

```powershell
git add backend/app/api/runs.py backend/app/services/results.py backend/tests/test_run_api.py backend/tests/test_results.py
git commit -m "feat: include provenance in run and result responses"
```

### Task 4: Define and render the shared frontend base panel

**Files:**
- Create: `frontend/src/types/analysisProvenance.ts`
- Create: `frontend/src/components/AnalysisProvenancePanel.vue`
- Create: `frontend/src/components/AnalysisProvenancePanel.test.ts`
- Modify: `frontend/src/api/runs.ts`

**Interfaces:**
- `AnalysisRunResponse.analysis_provenance?: AnalysisProvenance`.
- `<AnalysisProvenancePanel :provenance="AnalysisProvenance | undefined" />`.

- [ ] **Step 1: Write a failing component test**

```ts
it('shows backend-provided Welch step without deriving it', () => {
  const wrapper = mount(AnalysisProvenancePanel, { props: { provenance: fixture } })
  expect(wrapper.text()).toContain('4 s Hann，50% overlap（2 s 步进）')
  expect(wrapper.text()).toContain('配置指纹')
  expect(wrapper.text()).toContain('实际分析区间：5.000–15.000 s')
})
```

- [ ] **Step 2: Run the test and verify it fails**

Run: `npm run test -- --run frontend/src/components/AnalysisProvenancePanel.test.ts`

Expected: FAIL because the component/type does not exist.

- [ ] **Step 3: Implement API types and the panel**

```ts
export interface AnalysisProvenance {
  contract_version: 'analysis-provenance-v1'
  status: string
  config_sha256: string
  requested_range: TimeRange | null
  actual_range: TimeRange | null
  welch: { segment_s: number; window: string; overlap_fraction: number; step_s: number } | null
  extensions: AnalysisProvenanceExtension[]
}
```

The component renders Chinese labels in a responsive definition-list layout: algorithm/implementation versions, configuration fingerprint, requested and actual analysis ranges, selected channel/mapping, reference, sampling rate, filter, Welch, frequency, and quality. Format numbers and labels only. Render `—` for backend-null fields. Do not contain PSD integration, ratio math, overlap-to-step math, or channel-role inference.

- [ ] **Step 4: Run frontend type and component tests**

Run: `npm run typecheck && npm run test -- --run frontend/src/components/AnalysisProvenancePanel.test.ts`

Expected: PASS.

- [ ] **Step 5: Commit shared base rendering**

```powershell
git add frontend/src/types/analysisProvenance.ts frontend/src/api/runs.ts frontend/src/components/AnalysisProvenancePanel.vue frontend/src/components/AnalysisProvenancePanel.test.ts
git commit -m "feat: add shared analysis provenance panel"
```

### Task 5: Render typed evidence extensions and migrate the algorithm debug dialog

**Files:**
- Create: `frontend/src/components/AnalysisProvenanceExtensions.vue`
- Modify: `frontend/src/components/AlgorithmMetricDebugDialog.vue`
- Modify: `frontend/src/components/AlgorithmMetricDebugDialog.test.ts`

**Interfaces:**
- `<AnalysisProvenanceExtensions :extensions="provenance.extensions" />` renders `spectral_band_power` and `metric_inputs_output`.
- `AlgorithmMetricDebugDialog` receives `run.analysis_provenance` and uses no locally derived base metadata.

- [ ] **Step 1: Write failing migration tests**

```ts
it('renders base evidence from the shared provenance panel', () => {
  const wrapper = mount(AlgorithmMetricDebugDialog, { props: completedDynamicMetricProps })
  expect(wrapper.findComponent(AnalysisProvenancePanel).exists()).toBe(true)
  expect(wrapper.text()).toContain('频段积分')
  expect(wrapper.text()).toContain('Theta 功率')
})

it('does not render an independently derived Welch step when provenance is absent', () => {
  const wrapper = mount(AlgorithmMetricDebugDialog, { props: runWithoutProvenanceProps })
  expect(wrapper.text()).not.toContain('（2 s 步进）')
})
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run: `npm run test -- --run frontend/src/components/AlgorithmMetricDebugDialog.test.ts`

Expected: FAIL because the dialog still owns the duplicated base display.

- [ ] **Step 3: Implement finite extension renderers and migrate the dialog**

`AnalysisProvenanceExtensions` must have an explicit switch on backend `kind`:

```ts
case 'spectral_band_power': // backend band power / RBP values only
case 'metric_inputs_output': // backend input snapshot and backend output only
default: return null
```

Render the first extension as “频段积分” with absolute power and RBP; render the second as “算法输入与输出” with labels/units supplied by the API. In `AlgorithmMetricDebugDialog`, replace duplicated versions/config/range/filter/Welch/sample-rate/quality markup with `<AnalysisProvenancePanel>`, then place `<AnalysisProvenanceExtensions>` below it. Keep the existing raw PSD section but make it purely a collapse/format view of backend values.

- [ ] **Step 4: Run focused frontend tests**

Run: `npm run test -- --run frontend/src/components/AnalysisProvenancePanel.test.ts frontend/src/components/AlgorithmMetricDebugDialog.test.ts`

Expected: PASS.

- [ ] **Step 5: Commit dialog migration**

```powershell
git add frontend/src/components/AnalysisProvenanceExtensions.vue frontend/src/components/AlgorithmMetricDebugDialog.vue frontend/src/components/AlgorithmMetricDebugDialog.test.ts
git commit -m "refactor: use shared evidence panel in algorithm debug"
```

### Task 6: Verify compatibility and document the completed schema

**Files:**
- Modify: `docs/superpowers/specs/2026-09-15-analysis-provenance-debug-design.md`
- Test: existing backend and frontend suites

- [ ] **Step 1: Add a completed implementation note to the design spec**

Document the response field name, the `analysis-provenance-v1` contract version, the two initially supported extension kinds, and the explicit backend `welch.step_s` rule. State that new algorithm-specific evidence requires a new backend extension kind and renderer, never frontend science.

- [ ] **Step 2: Run complete verification gates**

Run:

```powershell
pytest backend/tests -q
Set-Location frontend; npm run typecheck; npm run test -- --run; npm run build; Set-Location ..
git diff --check
```

Expected: all tests/builds pass and no whitespace errors occur.

- [ ] **Step 3: Inspect the diff for scientific-contract regressions**

Run:

```powershell
git diff main...HEAD -- backend/app/eeg_core backend/app/services/spectral_analysis.py
git status --short
```

Expected: the only spectral-contract change is the additive `welch_step_s` metadata; no PSD, band boundary, preprocessing, unit, or time-window calculations change.

- [ ] **Step 4: Commit documentation and verification result**

```powershell
git add docs/superpowers/specs/2026-09-15-analysis-provenance-debug-design.md
git commit -m "docs: record analysis provenance debug schema"
```

## Self-review

- Spec coverage: Tasks 1–3 establish the additive backend contract and return it consistently. Tasks 4–5 establish the uniform base panel and finite extension path. Task 6 covers schema documentation and all required regressions.
- Placeholder scan: this plan defines the exact helper/component interfaces, extension kinds, tests, commands, and commit boundaries; it contains no deferred implementation markers.
- Type consistency: backend emits `analysis_provenance`, frontend names it `AnalysisProvenance`, and all extension renderers consume `AnalysisProvenanceExtension[]`. The declared Welch field is `welch.step_s` in the public projection, sourced from backend `welch_contract.welch_step_s`.
