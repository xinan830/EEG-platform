## Context

The lab must answer whether a clean scheduler can keep playback targets responsive while RBP work is delayed, overloaded, cancelled, duplicated, or completed out of order. The current Brain Platform is not a valid first host because its legacy queues and UI lifecycles would confound that experiment.

## Goals / Non-Goals

**Goals:**

- Build a standalone project boundary with independent build, runtime, and test environments.
- Prove deterministic target generation, bounded admission, single-flight, cancellation, overload behavior, result storage, and diagnostics without real EEG or UI dependencies.
- Assign playback target generation to a playback coordinator and RBP admission/resource ownership to one Python RBP scheduler.
- Add real EEG, independent scientific RBP, and WPF/SciChart only after the scheduler gates pass.

**Non-Goals:**

- Modifying or registering with Brain Platform.
- Reusing old TNK/RBP APIs, Analysis Run, Combined Strategy, old dynamic-window scheduling, or old analysis views.
- Static analysis, stateful algorithms, ICA, ERP, plugin infrastructure, or clinical interpretation.

## Decisions

1. **Independent repository and solution.** The lab has its own Git root, .NET solution, Python package/environment, test projects, and run commands. A folder/module inside Brain Platform was rejected because project-level dependencies and navigation registration would remain ambiguous.
2. **Phase 0 has no WPF, SciChart, real EEG, or Python scientific dependency requirement.** A virtual clock and synthetic source emit 1,000+ deterministic windows. The scheduler core is tested as a pure library/service boundary before adapters are introduced.
3. **Single scheduling authority per concern.** The playback coordinator owns the clock, cursor, and target generation. The Python RBP scheduler is the only RBP admission authority and owns its bounded queue, single-flight map, worker limit, cancellation release, and resource budget. WPF later subscribes to targets/results but does not create RBP workers.
4. **Different lane policies.** The waveform lane keeps only the newest pending display frame and never waits for RBP. The RBP lane uses bounded calculation admission, exact-target deduplication, expiry/overload policy, and subscriber-scoped cancellation.
5. **Out-of-order calculation, ordered projection.** Workers may complete and publish in any order. A result store keys entries by recording identity and half-open sample range; the view queries/sorts by window start and leaves gaps for skipped or unavailable windows. No slow window can block a newer completed window.
6. **Separate scientific and view identities.** CalculationKey includes recording, montage/channel order, sample bounds, sampling rate, units, preprocessing fingerprint, algorithm version, and parameters. ViewGeneration changes only on context changes such as recording/montage/configuration changes, restart, or seek policy boundaries; normal playback ticks do not change it.
7. **Compatibility-based preparation sharing.** Identical scientifically compatible preparation requests use single-flight/cache reuse. Display-only filtering, display downsampling, or different scientific filters remain separate preparations. Raw read count, preparation count, scientific compute count, and cache hits are recorded independently.
8. **Independent Phase 2 science.** The lab's recording reader, preparation pipeline, and Python RBP worker are new adapters. NumPy/SciPy/MNE and documented formula references may be used, but old business APIs and old scheduler code are prohibited.
9. **Bounded storage and failure semantics.** Caches have explicit item/byte limits and eviction diagnostics. Missing, rejected, cancelled, expired, and failed windows remain structured gaps; no zero fill, prior-value fill, or cross-gap line is allowed.

## Risks / Trade-offs

- [Risk] A separate repository duplicates setup work → keep the lab small, document one-command setup, and defer production integration until evidence exists.
- [Risk] Two process boundaries can hide timing → emit correlation IDs and monotonic timestamps at every target, queue, preparation, compute, and publication transition.
- [Risk] Out-of-order results make live charts appear non-monotonic → store by sample range and render a sorted projection with explicit gaps.
- [Risk] Single-flight cancellation could cancel work needed by another subscriber → use reference-counted/shared ownership and subscriber-scoped cancellation.
- [Risk] CPU overload changes results or starves playback → bound Python workers and queue capacity, record rejected/expired work, and keep playback target generation independent.

## Migration Plan

1. Create the independent repository/project skeleton and deterministic Phase 0 core.
2. Pass virtual-clock scheduler tests, including 1,000-window overload and fault runs.
3. Add the standalone recording reader, compatible preparation cache, and independent Python RBP worker.
4. Validate scientific values, units, channel order, gaps, and provenance against reference fixtures.
5. Add the independent WPF/SciChart workbench with waveform latest-frame and all-channel RBP stacked projection.
6. Run normal, delay, overload, seek, duplicate, cancellation, cache eviction, and process-failure experiments with P95/P99 evidence.
7. Keep rollback as deleting/disabling the lab repository; existing Brain Platform remains unchanged.

