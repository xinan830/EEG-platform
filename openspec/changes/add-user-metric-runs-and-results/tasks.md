## 1. Specification

- [x] Define a backend-only static user-metric run and scalar-result display.
- [x] Define result-card and chart-axis semantics without fabricated charts.

## 2. Implementation

- [ ] Add typed metric-run request/config and immutable definition-version lookup.
- [ ] Resolve curated spectral feature inputs and execute the saved graph in the queue worker.
- [ ] Persist scalar output, quality state, and artifact through AnalysisRun.
- [ ] Add ordinary-user run controls, polling, compact result card, and same-unit comparison chart.
- [ ] Extend Results Workbench rendering for definition-metric results.

## 3. Verification

- [ ] Add backend unit/API coverage for absolute and relative feature resolution, output units, missing channels, quality gates, and cache identity.
- [ ] Add frontend API/component coverage for run controls, terminal states, and chart eligibility.
- [ ] Run full backend/frontend checks, build, strict OpenSpec validation, archive, and post-archive validation.
