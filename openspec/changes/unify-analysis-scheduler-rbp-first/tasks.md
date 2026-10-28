## 1. Baseline and contracts

- [ ] 1.1 Freeze new TNK real-time adapters and document the current RBP, waveform and cursor behavior as the rollback baseline.
- [ ] 1.2 Define the cross-layer target envelope with trace ID, generation, recording/montage identity, sample rate, cursor sample, configuration fingerprint and cancellation/subscription identity.
- [ ] 1.3 Define the preparation identity and cache/single-flight contract, including channel order, units, preprocessing version and half-open sample bounds.
- [ ] 1.4 Add compatibility tests proving existing full Analysis Run and raw recording behavior are unchanged.

## 2. Unified scheduler foundation

- [ ] 2.1 Implement one playback context generation and target stream for seek, restart, recording change and configuration change.
- [ ] 2.2 Implement bounded admission and explicit policies for waveform preview, window-independent analysis, stateful analysis and batch work.
- [ ] 2.3 Implement exact in-flight deduplication and independent subscriber cancellation without cancelling required shared work.
- [ ] 2.4 Implement cooperative cancellation with resource-lease release and generation fences before replacement targets publish.
- [ ] 2.5 Add structured lifecycle diagnostics for queued, prepared, computed, published, cancelled, rejected and failed states.
- [ ] 2.6 Add deterministic scheduler tests for bounded backlog, superseded targets, out-of-order completion, context changes and capacity exhaustion.

## 3. Data preparation and RBP adapter

- [ ] 3.1 Connect the backend data layer to one authoritative recording/montage window read per exact RBP target.
- [ ] 3.2 Reuse one prepared window for the RBP computation and verify raw-read, preparation and cache-hit counters.
- [ ] 3.3 Implement the RBP adapter using the existing scientific preprocessing and five-band ratio contract without changing sample rate, units or channel order.
- [ ] 3.4 Return RBP results with actual half-open range, recording/montage identity, algorithm version, quality/evidence and preparation provenance.
- [ ] 3.5 Add backend parity tests for RBP values, failure states, source gaps and repeated exact targets.

## 4. Desktop waveform and RBP projection

- [ ] 4.1 Subscribe waveform preview to the unified target stream while keeping raw persistence independent of display and analysis.
- [ ] 4.2 Project RBP results into bounded per-channel histories keyed by generation and recording-relative window end time.
- [ ] 4.3 Handle out-of-order, duplicate, stale-generation, rejected and failed RBP windows without numeric-zero substitution.
- [ ] 4.4 Batch chart updates and verify cursor progression does not depend on RBP completion.
- [ ] 4.5 Add WPF projection and chart tests for chronological append, gaps, channel order and context reset.

## 5. UI versus backend performance diagnosis

- [ ] 5.1 Build the waveform/PSD/RBP A-B-C-D comparison using one recording and fixed playback settings.
- [ ] 5.2 Measure waveform frame interval P95/P99, Dispatcher delay, chart update duration, queue depth, CPU usage, result lag, raw reads and cache hits.
- [ ] 5.3 Determine whether observed stalls are primarily SciChart rendering, data binding, Python computation/IPC or cross-layer resource contention.
- [ ] 5.4 Set initial playback cadence, RBP step, queue bounds and analysis CPU/memory budgets from measured results and record them in the implementation documentation.

## 6. Acceptance and staged migration

- [ ] 6.1 Run focused backend, scheduler and WPF tests; build the desktop test project in an isolated output directory.
- [ ] 6.2 Run OpenSpec strict validation and git diff --check.
- [ ] 6.3 Replay the same real recording with waveform-only and waveform+RBP and verify continuity, shared preparation counts, bounded queue depth, publication coverage and result lag.
- [ ] 6.4 Enable the RBP adapter only after all acceptance evidence is recorded; preserve the rollback flag and existing full-run path.
- [ ] 6.5 Add the next window-independent algorithm only after RBP acceptance, and define a separate checkpoint adapter before migrating any stateful TNK algorithm.
- [ ] 6.6 Update HANDOFE.md with verified status, remaining GUI/hardware limitations, metrics and the next migration gate.

