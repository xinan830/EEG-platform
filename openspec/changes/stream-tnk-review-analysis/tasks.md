## 1. Contract and baseline

- [x] 1.1 Verify the existing TNK dynamic session/window identity and repository can represent/reconcile intermediate canonical frames without schema migration; revise this change before implementation if it cannot.
- [x] 1.2 Define diagnostic event fields and bounded-history policy for queue depth, replayed frames, actual result lag, delivery gaps, projection time, chart-update time, and playback tick timing.

## 2. Backend ordered frame delivery

- [x] 2.1 Add a typed TNK frame-event contract carrying stable window/session/configuration/implementation identity, actual half-open sample and time bounds, units, quality, evidence, failure, and delivery sequence.
- [x] 2.2 Extend TNK checkpoint replay to expose each canonical intermediate frame result in order while preserving existing state transitions, checkpoint boundaries, cancellation checks, and target-window output.
- [x] 2.3 Add bounded subscription delivery with resume/deduplication and explicit overflow or discontinuity ranges; reuse existing immutable window reads for reconciliation where possible.
- [x] 2.4 Keep existing target-only on-demand observation and full-Run behavior compatible; gate ordered playback delivery behind an opt-in capability with structured unsupported/capacity/failure outcomes.
- [x] 2.5 Add backend tests for stateful sequential replay parity, checkpoint restore, backward seek, context invalidation, duplicate/out-of-order event recovery, bounded overflow, and exact window identity/provenance.

## 3. Desktop playback and history projection

- [x] 3.1 Consume ordered TNK events without awaiting them on the playback dispatcher; keep the waveform clock, waveform reload, and algorithm scheduling lifecycles independent.
- [x] 3.2 Merge results by scientific window identity and actual analysis time; reject stale recording/configuration generations and preserve explicit unavailable/rejected/partial/gap states.
- [x] 3.3 Display latest completed TNK window time and lag separately from the moving cursor; show history coverage as complete, reconciling, or incomplete.
- [x] 3.4 Append only new TNK chart points and coalesce dispatcher notifications without dropping accepted scientific results; bound retained history and implement explicit reconciliation when delivery exceeds capacity.
- [x] 3.5 Measure line and stacked chart update costs at increasing history sizes; optimize any measured full-series replacement or prefix-scan hotspot without resetting the waveform or user chart range.
- [x] 3.6 Add desktop tests for playback continuing during delayed analysis, ordered append, deduplication, stale-session rejection, seek/config reset, lag display, overflow/gap recovery, and chart-range preservation.
- [x] 3.7 Start the ordered event consumer lazily on the first playback target so an idle cursor does not generate empty polling requests.
- [x] 3.8 Cancel a superseded active playback observation at a cooperative checkpoint while preserving normal active-work completion semantics for manual requests; add coordinator coverage for cancellation and latest-target replacement.

## 3A. Shared ANT stream execution

- [x] 3A.1 Add a session-scoped legacy stream bundle that reads raw Fz suffixes once and maintains the distinct Crimson/Zenlite preprocessing states.
- [x] 3A.2 Share Zenlite model inference across calmness, awareness, and spectral relaxation and reset/checkpoint the bundle with TNK replay state.
- [x] 3A.3 Use child-specific warm-up lengths (Crimson 5 s; Zenlite 1200/256 s) and cover chunk invariance, shared source reads, and warm-up behavior with focused tests.

## 4. Transport decision and acceptance

- [ ] 4.1 Capture paired waveform-only and waveform-plus-TNK playback baselines on the same local recording, role mapping, playback speed, and chart configuration; record p50/p95/p99 and queue/history coverage before enabling the new path.
- [ ] 4.2 Set measurable acceptance thresholds from the baseline before enabling the new playback path; include a real-desktop validation protocol and distinguish it from automated checks.
- [ ] 4.3 Compare measured polling request load and event-delivery latency with the established budget; retain polling when it passes, otherwise implement and validate the smallest resumable push transport that meets the same contract.
- [x] 4.4 Run backend and desktop focused suites, Release build, OpenSpec strict validation, and `git diff --check`; document unrelated environment/test failures separately.
- [ ] 4.5 Repeat the paired local-recording playback measurement and report waveform tick distributions, analysis lag, per-stage timings, event coverage, queue saturation, and chart costs.
- [ ] 4.6 Complete manual desktop acceptance for normal playback, deliberately delayed analysis, seek backward/forward, pause/resume, role/configuration change, cancellation, and delivery recovery; keep the capability disabled if any scientific parity or responsiveness gate fails.
- [ ] 4.7 Profile overlapping-window preparation only after playback acceptance; propose a separate scientific-contract change before enabling cross-window numeric reuse or changing any TNK formulas/window semantics.

## 5. Preview integrity and stage diagnostics

- [x] 5.1 Keep the existing uniform-stride response fields unchanged and add bounded per-channel minimum/maximum summaries for each decimated display bin, with explicit bin-center time and µV units.
- [x] 5.2 Render the optional extrema envelope behind the existing waveform line and label its values as display-bin ranges, not event timing.
- [x] 5.3 Add opt-in request-correlated backend timing for source preparation, window slicing/validation, display projection, channel count, output point count, stride, and envelope-bin count.
- [x] 5.4 Add focused backend and desktop tests for a transient omitted by uniform sampling, envelope time/unit/channel alignment, no-decimation behavior, and extrema rendering projection; run strict OpenSpec and diff checks.

## 6. On-demand STFT history projection

- [x] 6.1 Merge overlapping STFT window columns by recording-absolute time and preserve the backend frequency/unit/quality contract.
- [x] 6.2 Reset STFT history on backward seek, incompatible recording/configuration identity, and new on-demand session.
- [x] 6.3 Add focused desktop tests for overlap deduplication, append order, and context/reset behavior.
