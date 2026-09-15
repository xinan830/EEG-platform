# Unified Spectral Debug and Run Executor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Share backend provenance across every spectral debug view and make `RunService` lifecycle-only.

**Architecture:** Extend configured spectral payloads additively, reuse the existing provenance panel, then move unchanged analysis methods into a dependency-injected executor.

**Tech Stack:** FastAPI, Pydantic, NumPy, Vue 3, TypeScript, pytest, Vitest.

**Spec:** `docs/superpowers/specs/2026-09-15-unify-spectral-debug-and-run-executor-design.md`

## Global Constraints

- Preserve all EEG mathematics, units, API fields, cache keys, artifacts, and timing semantics.
- Provenance is backend authored; frontend is display only.
- Keep configured PSD/spectrogram requests synchronous.

### Task 1: Add configured spectral provenance and migrate debug panels

**Files:** `backend/app/services/spectral_analysis.py`, backend spectral tests, `frontend/src/types/spectrum.ts`, `frontend/src/types/spectrogram.ts`, `SpectrumAlgorithmDialog.vue`, `SpectrogramAlgorithmDialog.vue`, component tests.

- [ ] Write failing tests asserting both configured backend responses have `analysis_provenance.welch.step_s == 2.0` and both dialogs contain `AnalysisProvenancePanel`.
- [ ] Run focused tests and verify missing provenance/panel failures.
- [ ] Build provenance only from configured backend payload fields; add its response type; replace hard-coded base grids with the shared panel; retain only specialized evidence sections.
- [ ] Run focused backend/frontend tests and commit `feat: unify configured spectral debug provenance`.

### Task 2: Extract RunAnalysisExecutor

**Files:** create `backend/app/services/run_analysis_executor.py`; modify `backend/app/services/runs.py`; add executor equivalence tests.

- [ ] Write failing tests importing `RunAnalysisExecutor` and asserting configured spectrum, gate failure, and dynamic metric summaries match current Run behavior.
- [ ] Run focused tests and verify missing module failure.
- [ ] Move `_execute`, `_execute_definition_metric`, `_execute_dynamic_definition_metric`, and scalar helpers unchanged into executor; inject dependencies; delegate from `RunService`.
- [ ] Run Run, artifact, metric, spectral, and cache regression tests; commit `refactor: extract run analysis executor`.

### Task 3: Final verification

- [ ] Run backend pytest, frontend Vitest, production build, and `git diff --check`.
- [ ] Inspect spectral core diff for formula/timing changes and update the design implementation note.
- [ ] Commit documentation and merge through the normal verified branch workflow.
