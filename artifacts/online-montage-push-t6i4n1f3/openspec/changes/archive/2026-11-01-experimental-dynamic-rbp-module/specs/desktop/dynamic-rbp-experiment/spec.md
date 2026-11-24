## Purpose

This capability establishes an independent Scheduler Lab that proves dynamic playback, bounded RBP scheduling, compatible preparation reuse, and all-channel result projection without legacy Brain Platform dependencies.

## ADDED Requirements

### Requirement: Select project and recording before playback

The workbench SHALL require the operator to select an experiment project and one recording/data source before starting playback. The selected project and recording identity SHALL be attached to every playback target, preparation request, and RBP result.

#### Scenario: No recording is selected
- **WHEN** the operator opens the workbench without selecting a recording
- **THEN** playback remains stopped and no waveform or RBP target is submitted

#### Scenario: Recording is selected and started
- **WHEN** the operator selects a project, selects one recording, and starts the experiment
- **THEN** the playback coordinator uses that recording identity for subsequent windows and both waveform and RBP lanes receive the same selected context

### Requirement: Independent project boundary

The lab SHALL build and test as an independent Git project with its own .NET solution, Python environment/package, test projects, and run entry points. It SHALL NOT register with or call existing Brain Platform navigation, TNK/RBP business APIs, Analysis Run, Combined Strategy, or legacy dynamic-window schedulers.

#### Scenario: Run Phase 0 in isolation
- **WHEN** the operator runs the deterministic scheduler test entry point
- **THEN** the tests complete without a real EEG file, WPF, SciChart, or Brain Platform process

### Requirement: Separate playback and RBP scheduling ownership

The playback coordinator SHALL own the virtual/real playback clock, cursor, and target generation. The Python RBP scheduler SHALL be the sole RBP admission authority for bounded queueing, Single-flight, worker limits, expiry, cancellation release, and resource budgets. Playback target generation SHALL NOT wait for RBP completion.

#### Scenario: RBP is delayed
- **WHEN** RBP workers are slower than target generation
- **THEN** new playback targets continue to be generated and waveform latest-frame work remains admissible without creating a second RBP scheduler

### Requirement: Distinct waveform and RBP lane policies

The waveform lane SHALL merge pending display work to the newest frame. The RBP lane SHALL use bounded calculation admission, exact-target deduplication, expiry/overload policy, and subscriber-scoped cancellation.

#### Scenario: Waveform backlog occurs
- **WHEN** multiple waveform frames are pending
- **THEN** obsolete pending frames are coalesced and only the newest pending frame is retained

#### Scenario: RBP overload occurs
- **WHEN** the bounded RBP queue and worker budget are exhausted
- **THEN** the target is rejected or marked expired with a structured reason and no fabricated value

### Requirement: Out-of-order computation and ordered projection

RBP computation and result arrival MAY be out of order. The result store SHALL key entries by recording identity and half-open window sample range, and views SHALL project available entries in chronological order without waiting for slower earlier windows.

#### Scenario: Later window finishes first
- **WHEN** a later RBP window completes before an earlier slow window
- **THEN** the later result is stored and displayed at its time position while the earlier gap remains explicit

### Requirement: Calculation identity is separate from view generation

The lab SHALL define CalculationKey independently from ViewGeneration. Normal playback ticks SHALL NOT change ViewGeneration or invalidate reusable scientifically identical results. Context changes SHALL reject stale view publications without deleting reusable calculation results unless the policy explicitly evicts them.

#### Scenario: Same target repeats after normal ticks
- **WHEN** an identical scientific window is requested in a later playback tick
- **THEN** the scheduler reuses the CalculationKey result or Single-flight work even though the view has advanced

### Requirement: Compatible preparation sharing

The lab SHALL share a prepared result only when recording identity, montage/channel order, sample bounds, sampling rate, units, preprocessing fingerprint, and scientific configuration are compatible. Display-only filtering/downsampling and incompatible RBP preparation SHALL remain independent and SHALL report separate read, preparation, compute, and cache-hit counters.

#### Scenario: Display and RBP configurations differ
- **WHEN** waveform display requests downsampling or filtering that differs from RBP scientific preparation
- **THEN** the two preparation paths remain independent and neither result is relabeled as the other's scientific input

### Requirement: Bounded storage and structured gaps

The lab SHALL enforce cache item/byte limits and release resources after completion, cancellation, rejection, expiry, or failure. Missing, rejected, cancelled, expired, and failed windows SHALL remain structured gaps; the view SHALL NOT fill them with zero, a prior value, or a line across the gap.

#### Scenario: Shared task has multiple subscribers
- **WHEN** one subscriber cancels but another still requires the same Single-flight work
- **THEN** the shared work continues for the remaining subscriber and is released only after no subscriber requires it

### Requirement: Independent dynamic RBP science

After Phase 0 acceptance, the lab SHALL read recordings independently and compute Delta, Theta, Alpha, Beta, and Gamma relative shares for every valid selected channel, preserving absolute time, half-open sample ranges, sampling rate, units, channel order, quality, failure, and provenance.

#### Scenario: Valid real recording window
- **WHEN** a standalone recording reader supplies a valid window
- **THEN** the RBP worker returns five backend-owned band shares per valid channel with explicit identity, quality, and provenance

### Requirement: Observable stress evidence

The lab SHALL measure playback ticks, actual chart frame intervals, dispatcher delay, queue wait, preparation time, compute time, result lag, cache counts, CPU, and memory separately during normal, delayed, overloaded, seek, duplicate, cancellation, random-fault, cache-eviction, and process-failure experiments.

#### Scenario: Stress run completes
- **WHEN** the lab executes the defined deterministic and real-recording stress matrix
- **THEN** it produces reproducible evidence sufficient to distinguish playback, preparation, computation, publication, and resource contention

