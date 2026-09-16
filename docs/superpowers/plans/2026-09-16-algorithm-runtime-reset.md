# Algorithm Runtime Reset and Theta/Beta v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the fragmented official-algorithm execution path with one schema-driven runtime and make official Theta/Beta a single explicitly selected-channel algorithm without global channel mapping.

**Architecture:** Scientific primitives remain in the signal layer. Each algorithm owns its manifest, configuration, computation, static/dynamic runner, and validation. A generic runtime resolves a registered algorithm, validates its schema, executes it, and persists a normalized Run/Artifact result; generic services never branch on concrete algorithm IDs.

**Tech Stack:** Python, FastAPI, Pydantic, SQLAlchemy/SQLite, NumPy/SciPy, pytest, Vue 3/TypeScript, Vitest, OpenSpec.

**Spec:** `docs/superpowers/specs/2026-09-16-algorithm-runtime-reset-design.md`; OpenSpec change `openspec/changes/replace-algorithm-runtime-and-theta-beta-v2/`.

## Global Constraints

- Preserve frozen `offline-spectral-v3` preprocessing, Welch PSD, frequency boundaries, units, and quality rules.
- New Theta/Beta uses one real raw recording channel and never infers or relabels semantic channels.
- Scientific parameters are persisted in Run configuration; display-only chart history remains frontend-local.
- Failed scientific results are `null` with structured reasons, never zero.
- Historical Runs and Artifacts remain read-only/exportable and are never re-run through deleted code.
- No arbitrary Python plugins, clinical conclusions, global channel-role inference, or new spectral mathematics.

---

### Task 1: Freeze baselines and audit the current call graph

**Files:**
- Create: `backend/tests/fixtures/algorithm_runtime_baseline.json` (only anonymized scalar/shape fixtures)
- Create: `backend/tests/test_algorithm_runtime_architecture.py`
- Create: `docs/architecture/algorithm-runtime-reset-audit.md`
- Inspect: `backend/app/eeg_core/official_algorithms/`, `backend/app/services/runs.py`, `backend/app/services/run_analysis_executor.py`, `backend/app/models/recording.py`, `backend/app/api/recordings.py`, `frontend/src/components/AlgorithmDisplayWorkspace.vue`

**Interfaces:**
- Produces a table classifying every candidate module as canonical mathematics, runtime dependency, historical read support, or obsolete compatibility code.
- Produces frozen expected values for IAPF, existing Theta/Beta, PSD, RBP, FAA, and historical result retrieval.

- [ ] **Step 1: Inventory concrete algorithm branches and mapping consumers.**

  Run:

  ```powershell
  rg -n "algorithm_id|theta_beta|iapf|ChannelMapping|mapping|official_algorithm|official_definitions|official_algorithm_shadows" backend frontend
  ```

  Record each result and its caller in `docs/architecture/algorithm-runtime-reset-audit.md`.

- [ ] **Step 2: Capture baseline outputs before changing code.**

  Add pytest fixtures that call the current canonical scientific functions on deterministic synthetic signals and assert exact shapes, units, quality states, and existing tolerances. Do not include identifying EEG data.

- [ ] **Step 3: Add an architecture guard that initially documents known violations.**

  The test must scan generic runtime/service files and report concrete algorithm-ID branches. It is allowed to fail until Task 7 removes them; this creates an explicit cutover gate.

- [ ] **Step 4: Run the baseline tests and commit.**

  ```powershell
  python -m pytest backend/tests/test_algorithm_runtime_architecture.py backend/tests -q
  git add backend/tests/fixtures/algorithm_runtime_baseline.json backend/tests/test_algorithm_runtime_architecture.py docs/architecture/algorithm-runtime-reset-audit.md
  git commit -m "test: freeze algorithm runtime baselines"
  ```

### Task 2: Add typed runtime contracts and backend parameter schemas

**Files:**
- Create: `backend/app/algorithm_runtime/contracts.py`
- Create: `backend/app/algorithm_runtime/errors.py`
- Create: `backend/app/algorithm_runtime/parameter_schema.py`
- Create: `backend/app/algorithm_runtime/results.py`
- Create: `backend/tests/test_algorithm_runtime_contracts.py`

**Interfaces:**
- `AlgorithmManifest`
- `AlgorithmParameter`
- `AlgorithmConfigBase`
- `AlgorithmInputs`
- `AlgorithmResult` and `AlgorithmSeriesResult`
- `AlgorithmFailure(code: str, message: str, detail: dict | None)`

- [ ] **Step 1: Write failing contract tests.**

  Test that a parameter declares `key`, `label_zh`, `value_type`, `required`, `default`, `unit`, `affects_science`, and `visibility`; reject unknown value types and reject display parameters inside a scientific config.

- [ ] **Step 2: Implement Pydantic contracts.**

  Use strict numeric fields where scientific values are accepted. `AlgorithmResult.value` is nullable and carries `quality` plus an optional structured failure. The series result carries time centers and nullable values with matching lengths.

- [ ] **Step 3: Add schema serialization tests.**

  Assert JSON is frontend-safe and contains no callable, adapter, SQL, or Python module fields.

- [ ] **Step 4: Run and commit.**

  ```powershell
  python -m pytest backend/tests/test_algorithm_runtime_contracts.py -q
  git add backend/app/algorithm_runtime backend/tests/test_algorithm_runtime_contracts.py
  git commit -m "feat: add typed algorithm runtime contracts"
  ```

### Task 3: Create the canonical algorithm registry and runtime executor

**Files:**
- Create: `backend/app/algorithm_runtime/registry.py`
- Create: `backend/app/algorithm_runtime/executor.py`
- Create: `backend/app/algorithm_runtime/windows.py`
- Create: `backend/tests/test_algorithm_runtime_registry.py`
- Modify: `backend/app/services/run_analysis_executor.py`

**Interfaces:**
- `AlgorithmRegistry.register(algorithm: AlgorithmModule) -> None`
- `AlgorithmRegistry.get(algorithm_id: str, version: str | None = None) -> AlgorithmModule`
- `AlgorithmRuntime.execute(context: ExecutionContext) -> AlgorithmResult | AlgorithmSeriesResult`
- `build_windows(start_s: float, end_s: float, window_s: float, step_s: float) -> list[AnalysisWindow]`

- [ ] **Step 1: Test registry lookup and duplicate rejection.**

  Registering the same `algorithm_id` and scientific version twice must fail with a structured error. Unknown IDs and unsupported modes must fail before any signal load.

- [ ] **Step 2: Implement registry and runtime.**

  The executor receives a module object from the registry and invokes `resolve_inputs`, `execute_static`, or `execute_dynamic`. It must contain no `if algorithm_id == ...` or algorithm-specific output formatting.

- [ ] **Step 3: Implement deterministic window construction.**

  Validate positive window and step, clamp requested ranges to recording duration, preserve explicit time centers, and return no fabricated samples for an empty/invalid range.

- [ ] **Step 4: Route existing executor entry through the runtime for a test module.**

  Use a tiny test algorithm module in `backend/tests/fixtures/` to prove lifecycle, cancellation checkpoint, and normalized result serialization.

- [ ] **Step 5: Run and commit.**

  ```powershell
  python -m pytest backend/tests/test_algorithm_runtime_registry.py -q
  git add backend/app/algorithm_runtime backend/app/services/run_analysis_executor.py backend/tests
  git commit -m "feat: add generic algorithm runtime executor"
  ```

### Task 4: Move IAPF into a canonical algorithm module

**Files:**
- Create: `backend/app/algorithms/iapf/manifest.py`
- Create: `backend/app/algorithms/iapf/config.py`
- Create: `backend/app/algorithms/iapf/compute.py`
- Create: `backend/app/algorithms/iapf/runner.py`
- Create: `backend/app/algorithms/iapf/validation.py`
- Modify: `backend/app/algorithm_runtime/registry.py`
- Test: `backend/tests/test_iapf_algorithm_module.py`

**Interfaces:**
- `IapfConfig(channel: str, mode: Literal["static", "dynamic"], start_s: float, end_s: float, window_s: float | None, step_s: float | None)`
- `IapfAlgorithm.manifest`
- `IapfAlgorithm.execute_static(...)`
- `IapfAlgorithm.execute_dynamic(...)`

- [ ] **Step 1: Add equivalence tests against the frozen estimator.**

  For deterministic signals and existing anonymous fixtures, assert IAPF values, peak/COG fields, quality reasons, null behavior, and dynamic warm-up exactly match the baseline tolerances.

- [ ] **Step 2: Move the calculation without copying formulas.**

  The canonical module must call the existing `estimate_iapf` scientific implementation until the algorithm-specific implementation is moved and equivalence is proven; only one formula source may remain after cutover.

- [ ] **Step 3: Register IAPF and expose its parameter/result schema.**

  Include Chinese labels, channel selector sourced from recording labels, static/dynamic modes, requested range, trailing window, refresh step, output unit `Hz`, and quality state.

- [ ] **Step 4: Run and commit.**

  ```powershell
  python -m pytest backend/tests/test_iapf_algorithm_module.py -q
  git add backend/app/algorithms/iapf backend/app/algorithm_runtime/registry.py backend/tests/test_iapf_algorithm_module.py
  git commit -m "refactor: isolate canonical IAPF algorithm"
  ```

### Task 5: Implement official Theta/Beta v2 as a single-channel module

**Files:**
- Create: `backend/app/algorithms/theta_beta/manifest.py`
- Create: `backend/app/algorithms/theta_beta/config.py`
- Create: `backend/app/algorithms/theta_beta/compute.py`
- Create: `backend/app/algorithms/theta_beta/runner.py`
- Create: `backend/app/algorithms/theta_beta/validation.py`
- Modify: `backend/app/algorithm_runtime/registry.py`
- Test: `backend/tests/test_theta_beta_v2.py`

**Interfaces:**
- `ThetaBetaConfig(channel: str, mode: Literal["static", "dynamic"], start_s: float, end_s: float, window_s: float | None, step_s: float | None)`
- `ThetaBetaAlgorithm.execute_static(...) -> AlgorithmResult`
- `ThetaBetaAlgorithm.execute_dynamic(...) -> AlgorithmSeriesResult`

- [ ] **Step 1: Write failing single-channel tests.**

  Cover `Fz`, `Pz`, `O2`, and an arbitrary valid label. Assert the output stores that exact raw label and never returns `Fz/Pz/Oz` role bundles. Assert a dynamic run returns one value per valid time window.

- [ ] **Step 2: Test scientific calculation and failures.**

  Use the existing PSD and IAPF contracts. Assert theta is `[max(4, iapf - 6), iapf - 2]`, beta is `[iapf + 2, 30]`, output is dimensionless, and invalid PSD/IAPF/denominator returns `value=None` with a structured reason.

- [ ] **Step 3: Implement the module.**

  Resolve exactly one selected channel from raw recording labels. Reuse the canonical PSD loader and IAPF estimator; do not access recording mapping and do not infer aliases.

- [ ] **Step 4: Register the module and add backend schema.**

  The manifest must describe `Theta/Beta 比值`, one channel parameter, static/dynamic modes, analysis range, trailing window, step, dimensionless output, and null failure semantics.

- [ ] **Step 5: Run and commit.**

  ```powershell
  python -m pytest backend/tests/test_theta_beta_v2.py -q
  git add backend/app/algorithms/theta_beta backend/app/algorithm_runtime/registry.py backend/tests/test_theta_beta_v2.py
  git commit -m "feat: add single-channel Theta Beta v2"
  ```

### Task 6: Adapt user Definition graphs to the same runtime result contract

**Files:**
- Create: `backend/app/algorithms/user_definition/manifest.py`
- Create: `backend/app/algorithms/user_definition/runner.py`
- Modify: `backend/app/services/definition_executor.py`
- Modify: `backend/app/algorithm_runtime/registry.py`
- Test: `backend/tests/test_user_definition_runtime_adapter.py`

**Interfaces:**
- `UserDefinitionAlgorithm.resolve_inputs(...)`
- `UserDefinitionAlgorithm.execute_static(...)`
- `UserDefinitionAlgorithm.execute_dynamic(...)`

- [ ] **Step 1: Add adapter tests.**

  Existing valid user Definition runs must produce the same scalar/series values, units, quality state, and provenance while returning the new normalized result shape.

- [ ] **Step 2: Implement the adapter.**

  Wrap the existing controlled graph executor; preserve its type/unit/cycle/quality checks. Do not introduce `eval`, arbitrary Python, or algorithm-specific branches in generic runtime code.

- [ ] **Step 3: Register user definitions dynamically.**

  Resolve a user Definition/version from persisted identity, then expose its backend-authored schema through the same catalog contract.

- [ ] **Step 4: Run and commit.**

  ```powershell
  python -m pytest backend/tests/test_user_definition_runtime_adapter.py -q
  git add backend/app/algorithms/user_definition backend/app/services/definition_executor.py backend/app/algorithm_runtime/registry.py backend/tests/test_user_definition_runtime_adapter.py
  git commit -m "refactor: adapt user definitions to algorithm runtime"
  ```

### Task 7: Cut over Run lifecycle, provenance, and historical read behavior

**Files:**
- Modify: `backend/app/services/runs.py`
- Modify: `backend/app/api/runs.py`
- Modify: `backend/app/models/run.py`
- Modify: `backend/app/services/artifacts.py` (or current artifact persistence module)
- Create: `backend/tests/test_runtime_run_cutover.py`
- Create: `backend/tests/test_historical_run_read.py`

**Interfaces:**
- New Run requests resolve `algorithm_id`, `algorithm_version`, and config through `AlgorithmRuntime`.
- Persist `algorithm_schema_version`, explicit input choices, requested/actual range, and result schema identity.

- [ ] **Step 1: Add failing cutover tests.**

  Test static/dynamic official and user requests, idempotency, cache identity, cancellation, structured failures, and artifact metadata. Test a historical Run loads from stored JSON/NPZ without importing deleted calculator modules.

- [ ] **Step 2: Implement runtime-backed request resolution.**

  Remove official-specific resolver branches from `runs.py`; resolve all current requests through the registry and persist the exact config snapshot.

- [ ] **Step 3: Preserve historical read-only retrieval.**

  Historical records expose stored values, algorithm identity, quality, and artifact hashes. Reject re-run/clone requests that reference a non-current implementation with a structured `HISTORICAL_ALGORITHM_NOT_EXECUTABLE` error.

- [ ] **Step 4: Run and commit.**

  ```powershell
  python -m pytest backend/tests/test_runtime_run_cutover.py backend/tests/test_historical_run_read.py -q
  git add backend/app/services backend/app/api/runs.py backend/app/models/run.py backend/tests
  git commit -m "refactor: route runs through algorithm runtime"
  ```

### Task 8: Remove global ChannelMapping and obsolete execution paths

**Files:**
- Modify: recording models, migrations, import service, recording API, and frontend mapping components identified by Task 1 audit
- Delete: only files classified as obsolete compatibility/dead code in the audit
- Modify: `backend/tests/test_recording_migrations.py`, mapping tests, architecture guard

**Interfaces:**
- Recording keeps raw channel labels and order.
- New algorithm configs contain their own explicit channel fields.
- No current Run path reads `recording.mapping`.

- [ ] **Step 1: Add migration/read tests before deletion.**

  Upgrade a database containing mapping rows, verify repeated migration is idempotent, verify historical Run snapshots remain readable, and verify raw channel labels are unchanged.

- [ ] **Step 2: Remove active mapping writes and endpoints.**

  Delete import auto-mapping, mapping API/UI, and `OFFICIAL_CHANNEL_MAPPING_REQUIRED` only after all current consumers have moved to per-run channel parameters.

- [ ] **Step 3: Delete audited facades and old executable paths.**

  Remove `official_definitions.py`, `official_algorithm_shadows.py`, old `offline_metrics.py`/`processing/offline_analysis.py` entry paths, and any other file only when the audit proves no current import remains. Keep only historical serialization readers.

- [ ] **Step 4: Make the architecture guard pass.**

  Assert generic runtime/services contain no concrete algorithm-ID conditionals and deleted paths do not reappear.

- [ ] **Step 5: Run and commit.**

  ```powershell
  python -m pytest backend/tests/test_recording_migrations.py backend/tests/test_algorithm_runtime_architecture.py -q
  git add backend frontend
  git commit -m "refactor: remove global channel mapping and legacy paths"
  ```

### Task 9: Add unified catalog and schema-driven frontend configuration

**Files:**
- Create/modify: `frontend/src/api/algorithms.ts`
- Create: `frontend/src/types/algorithmRuntime.ts`
- Modify: `frontend/src/components/AlgorithmDisplayWorkspace.vue`
- Modify: related result-card and algorithm-picker components
- Test: `frontend/src/components/__tests__/AlgorithmDisplayWorkspace.runtime.spec.ts`

**Interfaces:**
- `getAlgorithms(): Promise<AlgorithmCatalogItem[]>`
- `AlgorithmCatalogItem { source, id, version, displayNameZh, description, parameters, modes, output }`
- `createRun({ algorithmId, algorithmVersion, config }): Promise<RunSummary>`

- [ ] **Step 1: Add failing UI tests.**

  Mock one official IAPF, one official Theta/Beta, and one user algorithm. Assert each selected algorithm renders its own channel/mode/range/window controls, official names are Chinese-readable, and display-history controls do not enter the Run payload.

- [ ] **Step 2: Implement catalog API/types.**

  Frontend reads labels, allowed values, units, and defaults from backend schema. It must not infer runnable state from IDs, DAG contents, or `quality_rules`.

- [ ] **Step 3: Implement independent configuration cards.**

  Each selected algorithm stores a separate scientific config; one common Run action submits all selected configs. Render raw channel labels returned by the recording metadata.

- [ ] **Step 4: Render backend results only.**

  Show selected raw channel, actual range, values, units, quality, and structured reasons. Do not calculate PSD, IAPF, Theta/Beta, averages, or unit conversions in Vue.

- [ ] **Step 5: Run and commit.**

  ```powershell
  npm run test -- --run frontend/src/components/__tests__/AlgorithmDisplayWorkspace.runtime.spec.ts
  npm run build
  git add frontend
  git commit -m "feat: use schema-driven algorithm configuration"
  ```

### Task 10: Full verification, documentation, and OpenSpec archive

**Files:**
- Modify: `docs/architecture/official-algorithm-migration.md`
- Modify: `docs/architecture/algorithm-runtime-reset-audit.md`
- Create: `docs/validation/algorithm-runtime-reset-2026-09-16.md`
- Modify: `openspec/changes/replace-algorithm-runtime-and-theta-beta-v2/tasks.md`

- [ ] **Step 1: Run targeted verification.**

  ```powershell
  python -m pytest backend/tests/test_iapf_algorithm_module.py backend/tests/test_theta_beta_v2.py backend/tests/test_runtime_run_cutover.py backend/tests/test_historical_run_read.py -q
  npm run test -- --run
  ```

- [ ] **Step 2: Run full gates.**

  ```powershell
  python -m pytest backend/tests -q
  npm run typecheck
  npm run build
  openspec validate replace-algorithm-runtime-and-theta-beta-v2 --strict --no-interactive
  git diff --check
  ```

- [ ] **Step 3: Record evidence.**

  Write pass counts, migration results, golden tolerances, artifact/history checks, and known non-blocking build warnings into `docs/validation/algorithm-runtime-reset-2026-09-16.md`.

- [ ] **Step 4: Archive only after all tasks are complete.**

  Mark every OpenSpec task complete, run:

  ```powershell
  openspec archive replace-algorithm-runtime-and-theta-beta-v2
  openspec validate --all --strict --no-interactive
  ```

- [ ] **Step 5: Commit final evidence.**

  ```powershell
  git add backend frontend docs openspec
  git commit -m "refactor: complete algorithm runtime reset"
  ```
