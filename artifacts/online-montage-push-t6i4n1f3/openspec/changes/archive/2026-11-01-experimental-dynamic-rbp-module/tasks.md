## 0. Independent project and pure scheduler core

- [x] 0.1 Create the independent Git root, .NET solution, Python package/environment, test projects, and documented run entry points outside Brain Platform navigation and routes.
- [x] 0.2 Define typed playback target, CalculationKey, ViewGeneration, window/sample identity, lane event, result status, cancellation, and diagnostic contracts.
- [x] 0.3 Implement a virtual clock and deterministic synthetic source capable of producing at least 1,000 windows without wall-clock waiting.
- [x] 0.4 Implement the playback coordinator that owns cursor/target generation and does not schedule Python RBP workers.
- [x] 0.5 Implement the Python RBP scheduler as the sole RBP admission authority with bounded queue, worker limit, Single-flight map, expiry policy, and resource-release handling.
- [x] 0.6 Implement pure scheduler tests for normal flow, out-of-order completion, duplicate targets, subscriber-scoped cancellation, overload, cache eviction, worker failure, process failure, and generation changes.

## 1. Lane policies and compatible preparation

- [x] 1.1 Implement the waveform latest-frame lane with pending-frame merge and no dependency on RBP completion.
- [x] 1.2 Implement the RBP bounded calculation lane with exact-target deduplication, expired-target rejection, and structured gaps for skipped work.
- [x] 1.3 Implement bounded preparation/result caches with byte/item limits, eviction, and independent read/preparation/compute/cache-hit counters.
- [x] 1.4 Implement compatibility checks so only identical scientific preparation identities share a result; display and RBP preparations remain independent when configurations differ.
- [x] 1.5 Implement an out-of-order result store keyed by recording identity and half-open sample range, with sorted projection and no head-of-line blocking.
- [x] 1.6 Add deterministic tests for CalculationKey reuse across normal ticks and ViewGeneration reset on context changes.

## 2. Real EEG and independent RBP science

- [x] 2.1 Add a standalone read-only EEG recording reader with explicit sample rate, absolute time, channel order, units, and source identity.
- [x] 2.2 Add independent scientific preparation and provenance snapshots without calling old Brain Platform services or APIs.
- [x] 2.3 Implement the Python RBP worker for every valid selected channel and Delta/Theta/Alpha/Beta/Gamma shares with explicit quality and failure states.
- [x] 2.4 Add reference-fixture tests for values, units, half-open ranges, montage/channel order, source gaps, invalid denominators, and reproducible provenance.

## 3. Independent WPF/SciChart workbench

- [x] 3.1 Create the separate workbench project and playback adapter without registering it in Brain Platform.
- [x] 3.2 Render waveform latest frames from the waveform lane and expose actual chart frame timing separately from playback ticks.
- [ ] 3.3 Render all valid channels as time-ordered five-band RBP stacked history with explicit gaps and no cross-gap lines.
- [ ] 3.4 Display scheduler, cache, preparation, compute, queue, and result-lag diagnostics without allowing chart updates to run scientific computation.
- [ ] 3.5 Add workbench tests for context reset, seek, channel labels, out-of-order results, duplicate results, unavailable windows, and bounded history.
- [x] 3.6 Require project and recording selection before starting playback and propagate the selected identities into target/result context.

## 4. Stress and architecture acceptance

- [ ] 4.1 Run deterministic 1,000-window normal, delayed, overloaded, duplicate, cancellation, random-fault, cache-eviction, and process-failure experiments.
- [ ] 4.2 Run real-recording continuous playback, rapid seek, restart, recording change, and repeated-target experiments.
- [ ] 4.3 Record playback ticks, actual chart frame intervals, UI/dispatcher delay, queue wait, preparation time, compute time, result lag, cache counts, CPU, and memory separately.
- [ ] 4.4 Verify waveform target progression remains independent when RBP is delayed or rejected, and verify no gap is filled or connected deceptively.
- [ ] 4.5 Publish a reproducible evidence report and run OpenSpec strict validation and git diff --check.

