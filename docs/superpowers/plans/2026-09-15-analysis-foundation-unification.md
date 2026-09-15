# Analysis Foundation Unification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Centralize frontend spectral transport, scientific unit presentation, and recording-dependent analysis validation without changing EEG output values.

**Architecture:** Browser API modules use the existing `request<T>` transport. A display-only TypeScript utility maps canonical API-unit strings to stable visible labels. Backend retains Pydantic models for schema-only checks and introduces one service helper for rules that need a real recording.

**Tech Stack:** FastAPI, Pydantic, Vue 3, TypeScript, Vitest, pytest.

**Spec:** `docs/superpowers/specs/2026-09-15-analysis-foundation-unification-design.md`

## Global Constraints

- Do not change `offline-spectral-v3`, numeric API units, frequency boundaries, channel order, or timing semantics.
- Frontend utilities format text only; they do not convert, normalize, integrate, or derive scientific values.
- Preserve legacy synchronous configured spectrum and spectrogram routes.
- Recording-dependent analysis validity is decided only by the backend.

---

### Task 1: Common API transport

**Files:**
- Modify: `frontend/src/api/spectrum.ts`
- Modify: `frontend/src/api/spectrogram.ts`
- Test: `frontend/src/api/spectrum.test.ts`
- Test: `frontend/src/api/spectrogram.test.ts`

**Interface:** Existing exported getters keep their signatures and call `request<T>(path, init)`.

- [ ] Write failing tests that assert configured calls retain URL encoding, POST method, and JSON body through the common request client.
- [ ] Run `npm run test -- --run src/api/spectrum.test.ts src/api/spectrogram.test.ts` and observe the old independent fetch behavior.
- [ ] Replace direct fetch/error parsing with `request<SpectrumResponse>` and `request<SpectrogramResponse>`; retain `URLSearchParams` and current request bodies.
- [ ] Re-run focused tests and commit `refactor: unify spectral API transport`.

### Task 2: Display-only scientific units

**Files:**
- Create: `frontend/src/utils/scientificDisplay.ts`
- Create: `frontend/src/utils/scientificDisplay.test.ts`
- Modify: `frontend/src/components/AnalysisProvenancePanel.vue`
- Modify: `frontend/src/components/AlgorithmMetricDebugDialog.vue`
- Modify: `frontend/src/components/SpectrumPsdChart.vue`
- Modify: `frontend/src/components/SpectrogramChart.vue`

**Interface:** `unitDisplay(unit: unknown): string` and `formatScientificValue(value: unknown, unit: unknown, digits?: number): string`.

- [ ] Write failing tests for `uV^2 -> µV²`, `uV^2/Hz -> µV²/Hz`, `dB re 1 uV^2/Hz -> dB re 1 µV²/Hz`, unknown-unit passthrough, and non-finite value as `—`.
- [ ] Run `npm run test -- --run src/utils/scientificDisplay.test.ts` and observe the missing utility failure.
- [ ] Implement a literal mapping dictionary with no numerical conversion; migrate only unit labels and tooltip suffixes in the named components.
- [ ] Run focused component tests and `npm run build`; commit `feat: unify scientific unit display`.

### Task 3: Recording-dependent backend validation

**Files:**
- Create: `backend/app/services/analysis_input_validation.py`
- Modify: `backend/app/services/spectral_analysis.py`
- Modify: `backend/app/services/definition_metric_runner.py`
- Test: `backend/tests/test_analysis_input_validation.py`
- Test: `backend/tests/test_spectrum_service.py`

**Interface:** `validate_recording_analysis_input(recording, sfreq, available_channels, start_s, end_s, channels, frequency_range=None) -> None` raises `AnalysisInputValidationError(code, message, details)`.

- [ ] Write failing tests for zero sampling rate, requested end beyond real duration, unavailable channel, and a frequency upper bound at/above Nyquist.
- [ ] Run `python -m pytest backend/tests/test_analysis_input_validation.py -v` and observe the missing-helper failure.
- [ ] Implement one helper for finite positive sampling rate, real-duration bounds, channel membership, and strict Nyquist ceiling; keep Pydantic duration/frequency-order validation untouched.
- [ ] Call it before spectral loading and translate errors through existing stable route contracts without fabricating a result.
- [ ] Run focused backend tests and commit `refactor: centralize recording analysis input validation`.

### Task 4: Full regression and documentation

**Files:**
- Modify: `docs/superpowers/specs/2026-09-15-analysis-foundation-unification-design.md`

- [ ] Document the completed transport, display, and validation boundaries.
- [ ] Run backend pytest, frontend Vitest, frontend production build, and `git diff --check`.
- [ ] Inspect `git diff main...HEAD -- backend/app/eeg_core backend/app/services/spectral_analysis.py` and confirm no mathematical or timing calculation changed.
- [ ] Commit `docs: record analysis foundation unification`.

## Self-review

- Task 1 centralizes browser transport while retaining synchronous endpoint behavior.
- Task 2 centralizes only visible text and has explicit no-conversion tests.
- Task 3 keeps schema rules and data-dependent rules distinct.
- Task 4 uses existing golden regressions as the scientific safety gate.
