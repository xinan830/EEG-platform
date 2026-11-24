## 1. Contract and baseline

- [x] 1.1 Verify the existing TNK dynamic session/window identity and repository can represent/reconcile intermediate canonical frames without schema migration; revise this change before implementation if it cannot.
- [x] 1.2 Define diagnostic event fields and bounded-history policy for queue depth, replayed frames, actual result lag, delivery gaps, projection time, chart-update time, and playback tick timing.

## 2. Backend ordered frame delivery

- [x] 2.1 Add a typed TNK frame-event contract carrying stable window/session/configuration/implementation identity, actual half-open sample and time bounds, units, quality, evidence, failure, and delivery sequence.
- [x] 2.2 Extend TNK checkpoint replay to expose each canonical intermediate frame result in order while preserving existing state transitions, checkpoint boundaries, cancellation checks, and target-window output.
- [x] 2.3 Add bounded subscription delivery with resume/deduplication and explicit overflow or discontinuity ranges; reuse existing immutable window reads for reconciliation where possible.
- [x] 2.4 Keep existing target-only on-demand observation and full-Run behavior compatible; gate ordered playback delivery behind an opt-in capability with structured unsupported/capacity/failure outcomes.
- [x] 2.5 Add backend tests for stateful sequential replay parity, checkpoint restore, backward seek, context invalidation, duplicate/out-of-order event recovery, bounded overflow, and exact window identity/provenance.
- [x] 2.6 Expose exact failed-frame ranges when event publication fails, retry one canonical frame from the desktop, and keep unrecovered history explicitly incomplete.

## 3. Desktop playback and history projection

- [x] 3.1 Consume ordered TNK events without awaiting them on the playback dispatcher; keep the waveform clock, waveform reload, and algorithm scheduling lifecycles independent.
- [x] 3.2 Merge results by scientific window identity and actual analysis time; reject stale recording/configuration generations and preserve explicit unavailable/rejected/partial/gap states.
- [x] 3.3 Display latest completed TNK window time and lag separately from the moving cursor; show history coverage as complete, reconciling, or incomplete.
- [x] 3.4 Append only new TNK chart points and coalesce dispatcher notifications without dropping accepted scientific results; bound retained history and implement explicit reconciliation when delivery exceeds capacity.
- [x] 3.5 Measure line and stacked chart update costs at increasing history sizes; optimize any measured full-series replacement or prefix-scan hotspot without resetting the waveform or user chart range.
- [x] 3.6 Add desktop tests for playback continuing during delayed analysis, ordered append, deduplication, stale-session rejection, seek/config reset, lag display, overflow/gap recovery, and chart-range preservation.
- [x] 3.7 Start the ordered event consumer lazily on the first playback target so an idle cursor does not generate empty polling requests.
- [x] 3.8 Cancel a superseded active playback observation at a cooperative checkpoint while preserving normal active-work completion semantics for manual requests; add coordinator coverage for cancellation and latest-target replacement.
- [x] 3.9 Throttle presentation-only TNK chart cursor refreshes to 4 Hz while preserving waveform cadence and every accepted result-history update.
- [x] 3.10 Stop hidden/unloaded chart subscriptions and rendering updates, synchronize latest snapshot/cursor on reactivation, and add focused lifecycle regression coverage (not executed at user request). Desktop and test-project compilation passed with 0 errors; existing package/analyzer warnings remain. OpenSpec strict validation and diff checks passed; real playback acceptance remains task 3.11.
- [ ] 3.11 Manually repeat RBP 30 s then fatigue 30 s; confirm old RBP cursor diagnostics stop and assess remaining waveform stalls independently.

## 3A. Shared ANT stream execution

- [x] 3A.1 Add a session-scoped legacy stream bundle that reads raw Fz suffixes once and maintains the distinct Crimson/Zenlite preprocessing states.
- [x] 3A.2 Share Zenlite model inference across calmness, awareness, and spectral relaxation and reset/checkpoint the bundle with TNK replay state.
- [x] 3A.3 Use child-specific warm-up lengths (Crimson 5 s; Zenlite 1200/256 s) and cover chunk invariance, shared source reads, and warm-up behavior with focused tests.

## 3B. Shared WPF source access

- [x] 3B.1 Reuse bounded, identity-validated WPF facts and batch indexes across per-window readers; remove duplicate indexing and cover cache reuse/source-change invalidation.

## 3C. Unified EEG data access contract

- [x] 3C.1 Map every current historical read entry point (`load_data`, `open_data_reader`, `open_sample_reader`, preview/spectrum) and separate raw persistence, live display, and HTTP display-filter ownership.
- [x] 3C.2 Define the identity-bound sample access session contract, including coordinates, units, channel order, gaps, lifecycle, per-format concurrency, cancellation, and live C#/Python boundary.
- [x] 3C.3 Implement the source-independent backend access-session API and historical WPF/BDF/EDF adapters with bounded lifecycle, source identity validation, explicit gap reporting, and per-format concurrency/cancellation semantics.
- [x] 3C.4 Migrate TNK, mapped preprocessing, preview/spectrum, and waveform playback to the historical access session; preserve their existing scientific inputs and display behavior. Independent spectral-reference reads remain separate by design so parity checks do not reuse the production adapter.
- [x] 3C.5 Delete `open_sample_reader`, `open_data_reader`, and other recording-owned reader entry points after all callers migrate; do not retain parallel legacy readers or silent fallbacks.
- [x] 3C.6 Implement the live acquisition adapter over an explicit bounded C#/Python process boundary using the same sample contract; keep raw persistence independent from display retention.
- [x] 3C.7 Validate the shared sample-block type, source-identity-scoped coordinates, V units, channel order, unavailable reasons, cancellation, and source/session invalidation across historical and live adapters.
- [x] 3C.8 Verify TNK retains one access session across consecutive windows without changing algorithm inputs or source identity.
- [x] 3C.9 Gate multi-algorithm scheduling work on acceptance of the unified data access layer; scheduling must consume the access contract rather than own file readers.
- [x] 3C.10 Bulk-read consecutive tiny WPF payloads inside the access adapter while preserving sample bounds, channel order, units, gaps, cancellation, and source validation.
- [x] 3C.11 Bound database-backed cancellation checks at the access-session boundary and acquire the on-demand compute lease only after source preparation completes.
- [x] 3C.12 Verify the WPF desktop review adapter against the shared logical contract: map source counters to recording-relative sample time, distinguish source gaps from missing channel values, scope reads to the opened recording identity, and preserve channel order and units. `LocalRawRecordingReaderTests` covers non-zero counter origin, explicit counter gaps, channel order/units, and rejection when the selected session ID differs from the directory manifest; focused suite passed. Manual playback acceptance remains in 4.6.

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
- [x] 5.3 Add opt-in request-correlated backend timing for bounded window read/display filtering, window slicing/validation, display projection, channel count, output point count, stride, and envelope-bin count.
- [x] 5.4 Add focused backend and desktop tests for a transient omitted by uniform sampling, envelope time/unit/channel alignment, no-decimation behavior, and extrema rendering projection; run strict OpenSpec and diff checks.
- [x] 5.5 Split signal preview from algorithm scientific preparation: use bounded `load_window()` access with the display causal-IIR contract, remove the preview preparation facade, and verify overlapping preview/algorithm requests have independent paths.
- [x] 5.6 Make display data access block-oriented: read one-second source blocks, preserve causal filter state, and emit 50 ms playback frames from memory so WPF access is not invoked once per display frame.

## 6. On-demand STFT history projection

- [x] 6.1 Merge overlapping STFT window columns by recording-absolute time and preserve the backend frequency/unit/quality contract.
- [x] 6.2 Reset STFT history on backward seek, incompatible recording/configuration identity, and new on-demand session.
- [x] 6.3 Add focused desktop tests for overlap deduplication, append order, and context/reset behavior.
