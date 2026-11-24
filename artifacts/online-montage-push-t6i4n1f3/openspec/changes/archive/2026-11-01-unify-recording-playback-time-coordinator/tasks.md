## 1. Contract and core context

- [x] 1.1 Add the typed recording-session playback context and immutable cursor snapshot with recording identity, integer cursor sample, sampling rate, derived absolute seconds, play state, playback rate, and generation.
- [x] 1.2 Implement monotonic-clock play, pause, seek, rate-change, completion, reset, range validation, and generation rules; add focused boundary tests for sample conversion, end clamping, seek, pause/resume, and ordinary-tick generation stability.
- [x] 1.3 Add subscriber and lifecycle tests proving cursor publication is non-blocking, ordered per context, and rejects out-of-range or stale recording identities.

## 2. Workspace integration

- [x] 2.1 Locate each recording workspace composition root and create exactly one playback context per host workspace; expose constructor injection so a future TNK panel hosted inside Review can receive Review's context, while current independent Review and TNK windows keep separate contexts.
- [x] 2.2 Migrate Review playback controls and the single playback driver to the shared context while preserving bounded waveform cache loading, display-only filtering, gaps, and current absolute-time behavior.
- [x] 2.3 Remove Review-owned duplicate cursor calculations and route analysis binding requests through shared cursor samples plus generation/revision fencing.

## 3. TNK and algorithm consumers

- [x] 3.1 Migrate TNK workbench waveform position, play/pause, seek, rate and completion behavior to the shared context; remove its local `Stopwatch` and playback `DispatcherTimer` as time sources.
- [x] 3.2 Change algorithm detail and catalog dynamic-preview consumers to project the shared cursor for display and manual step only; the 1-second timer is now only a coordinator-driven wakeup for standalone dynamic preview, while request debounce remains separate.
- [x] 3.3 Delete the unused playback target publication event and envelope; route targets directly through the existing request coordinator, which remains request-only.
- [x] 3.4 Fence TNK and dynamic-window results by recording identity, generation, target cursor, scientific configuration identity and window identity; add tests for seek-back, rapid target replacement, and slow-result arrival. Dynamic requests carry a desktop analysis-context generation that is excluded from transport JSON but participates in request/cache identity.

## 4. Cleanup and boundary verification

- [x] 4.1 Remove all old local playback-clock write paths and update affected display bindings, commands, and lifecycle disposal without retaining a runtime compatibility branch.
- [x] 4.2 Add architecture/source checks proving one playback driver per recording session, no algorithm timer creates cursor targets, and no backend playback state or scientific computation moved into WPF. Added `desktop-client/scripts/verify_playback_architecture.ps1`.
- [x] 4.3 Update the current architecture documentation and OpenSpec references with ownership, cursor units, generation semantics, and viewer/analysis separation.

## 5. Validation

- [x] 5.1 Run focused WPF playback, Review, TNK, dynamic-window, and request-race tests; record failures as code defects or environment limitations. Playback-coordinator/dynamic-window suites passed 23 tests, `TnkWorkbenchTests` passed 19 tests, and `ReviewOnDemandAnalysisTests` passed 13 tests.
- [x] 5.2 Run OpenSpec strict validation, Release WPF build, and `git diff --check`. All passed; build emitted only existing preview SDK/package and analyzer warnings.
- [ ] 5.3 Perform manual same-recording acceptance for play, pause, seek, rate change, Review plus TNK cursor equality, delayed algorithm result, and waveform continuity; capture desktop/backend logs without submitting EEG data.
