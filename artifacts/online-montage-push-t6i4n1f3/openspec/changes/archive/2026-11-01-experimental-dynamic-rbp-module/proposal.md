## Why

The existing Brain Platform mixes playback, waveform rendering, TNK/RBP execution, and result delivery, so it cannot isolate scheduler correctness from legacy coupling or UI contention. This change establishes a separate EEG Scheduler Lab that proves the scheduling architecture independently before any production integration is considered.

## What Changes

- Create an independent Git project with a .NET solution, Python environment/package, test projects, and explicit run entry points.
- Keep the lab out of the existing Brain Platform navigation, routes, TNK pages, Analysis Run pipeline, and legacy RBP services.
- Define playback-target generation separately from Python RBP admission and resource scheduling.
- Use a deterministic virtual clock and synthetic source for Phase 0; do not require real EEG, WPF, or SciChart to test the scheduler core.
- Use a latest-frame waveform lane and an independently bounded Python RBP calculation lane.
- Allow computation and result arrival out of order; store results by recording identity and window sample range, then render them in time order.
- Separate CalculationKey from ViewGeneration; normal playback ticks SHALL NOT invalidate reusable scientific results.
- Share prepared data only when scientific preparation identities are compatible; keep display and RBP preparation independent otherwise.
- Add bounded caches, overload limits, resource release, subscriber-scoped cancellation, process-failure handling, and structured diagnostics.
- Later add a standalone recording reader, scientific preprocessing, all-valid-channel five-band RBP worker, and a separate WPF/SciChart workbench.
- **BREAKING**: none to the existing Brain Platform because the lab is a separate project and has no production registration.

## Capabilities

### New Capabilities

- scheduler-lab/dynamic-rbp: an independent, deterministic scheduler core and staged dynamic RBP laboratory with waveform and RBP lanes.

### Modified Capabilities

- None.

## Impact

- New sibling project/repository artifacts, .NET and Python test environments, deterministic scheduler tests, and later standalone EEG/RBP/workbench components.
- No dependency on old TNK/RBP business APIs, old dynamic-window coordinators, Combined Strategy, Analysis Run, or old analysis views.
- Scientific calculations remain explicit and independently implemented in the lab's Python worker; the existing project is reference material only.
- No database migration and no production route or navigation change.

