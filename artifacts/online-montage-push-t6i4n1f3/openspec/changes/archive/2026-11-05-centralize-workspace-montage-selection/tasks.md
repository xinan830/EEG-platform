## 1. Workspace authority

- [x] 1.1 Define the workspace-scoped montage selection contract and lifecycle; bind it to one recording identity and validated immutable catalogue.
- [x] 1.2 Create one selection instance when a recording-review workspace opens and pass that same instance to waveform and algorithm workspace composition.
- [x] 1.3 Initialize selection from the existing documented acquisition-default rule; isolate simultaneous workspaces and release subscriptions on close/switch.

## 2. Remove duplicate state

- [x] 2.1 Move the review montage control to the workspace authority and bind it to the single writable selection.
- [x] 2.2 Change waveform consumers and cache identity to observe the shared snapshot/revision.
- [x] 2.3 Change algorithm panel contexts and request builders to read the same snapshot; retain only algorithm-specific role/channel choices.
- [x] 2.4 Delete independent review/algorithm montage-selection properties and event-copy synchronization paths; do not add compatibility forwarding or shadow state.
- [x] 2.5 Preserve montage catalogue persistence and immutable acquisition/analysis snapshots as separate data responsibilities.

## 3. Refresh and provenance

- [x] 3.1 On selection change, invalidate montage-derived waveform caches and cancel or reject prior-revision in-flight waveform work.
- [x] 3.2 On selection change, refresh all affected algorithm panels and cancel or reject stale Run/preview/window results without changing algorithm formulas.
- [x] 3.3 Build every official Run from one captured selection snapshot, preserving explicit recording ID, montage ID/fingerprint, source role mapping, resolved channel order, time range, units, and algorithm parameters.
- [x] 3.4 Report missing or incompatible algorithm roles as structured unavailable/errors; prohibit positional or prior-montage fallback.
- [x] 3.5 Keep the last complete waveform visible during montage-frame replacement and atomically publish the new montage frame.

## 4. Focused verification

- [x] 4.1 Add tests proving one change updates waveform and every algorithm consumer, while independent recordings remain isolated.
- [x] 4.2 Add tests for role remapping, unavailable roles, and exact ordered Run inputs/provenance from the selected snapshot.
- [x] 4.3 Add race tests proving late results from an older selection cannot publish as current; test cache reuse only for exact recording/range/montage/processing identity.
- [x] 4.4 Run focused desktop tests and WPF build; record any unverified interactive review/playback acceptance explicitly.
- [x] 4.5 Validate the OpenSpec change and sync the accepted delta requirements when implementation is complete.

## Validation record

- Feature-focused desktop tests: 47 passed, 0 failed.
- WPF project build: passed. .NET preview SDK and existing NU1701 package-compatibility warnings remain.
- OpenSpec strict validation: change valid; all 85 main specs passed.
- A broader Review-focused filter reported 95 passed and 2 failed: `Analysis_status_notifications_do_not_republish_result_and_detach_clears_panel` and `Dragging_exclusively_owns_position_then_restores_previous_playback_state(wasPlaying: True)`. Both failed again individually; their exercised paths do not cover montage selection, and baseline behavior was not tested.
- Full test suite was not run. Manual WPF review/playback interaction was not performed.
- Montage-switch follow-up: the prior complete frame is retained until its replacement is ready; `RecordingReviewSessionTests` passed 13/13, and the user confirmed manual montage-switch testing on 2026-11-05.
