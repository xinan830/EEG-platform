## 1. Specification

- [x] Define a backend-only static user-metric run and scalar-result display.
- [x] Define result-card and chart-axis semantics without fabricated charts.

## 2. Implementation

- [x] Add typed metric-run request/config and immutable definition-version lookup.
- [x] Resolve curated spectral feature inputs and execute the saved graph in the queue worker.
- [x] Persist scalar output, quality state, and artifact through AnalysisRun.
- [x] Add dynamic 10 s / 1 s backend series semantics and NPZ persistence.
- [x] Remove execution from the definition library and add the waveform-and-algorithms workspace.
- [x] Add static result cards and dynamic unit-safe trend charts to that workspace.
- [ ] Extend Results Workbench rendering for definition-metric results.

## 3. Verification

- [x] Add backend unit/API coverage for static/dynamic feature resolution, output units, missing channels, quality gates, and cache identity.
- [x] Add frontend API/component coverage for workspace selection, terminal states, and chart semantics.
- [ ] Run full backend/frontend checks, build, strict OpenSpec validation, archive, and post-archive validation.
