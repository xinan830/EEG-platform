## 1. Contract and storage

- [x] 1.1 Add `EventDefinition`, `RecordingEvent`, source enum, schema version, and validation contracts.
- [x] 1.2 Add versioned local stores with atomic writes and non-destructive recovery behavior.
- [ ] 1.3 Define sample-counter to Recording-relative sample conversion, point/interval semantics, and gap/unavailable behavior for counter reset, reconnect, and discontinuity segments.
- [ ] 1.4 Add model, serialization, migration, and restart round-trip tests before UI integration.

## 2. Shortcut and services

- [x] 2.1 Implement `EventDefinitionService` for CRUD, enable/disable, Code uniqueness, and provenance rules.
- [x] 2.2 Implement `RecordingEventService` for create, update, delete, time-range lookup, and combined filtering by RecordingId, sample range, definition/code, and source.
- [x] 2.3 Implement `ShortcutRegistry` with Global/Acquisition/Review scopes and structured conflict results.
- [x] 2.4 Add tests for shortcut conflicts, disabled-event release, system events, and externally sourced read-only events.

## 3. Settings UI

- [x] 3.1 Register 事件设置 in the settings list and navigation.
- [ ] 3.2 Add event definition list view with search, enabled state, source, color, shortcut, and actions.
- [ ] 3.3 Add new/edit event view with Code, name, description, color picker, shortcut capture, and validation messages.
- [ ] 3.4 Add system-event and referenced-event deletion restrictions.

## 4. Acquisition integration

- [x] 4.1 Expose enabled event definitions and event commands from the acquisition workspace ViewModel.
- [ ] 4.2 Add the shared event toolbar and shortcut handling without blocking the device read loop or raw writer.
- [ ] 4.3 Capture the current sample coordinate before scheduling asynchronous persistence; persist manual button/shortcut events without reading a later “current sample” from the background task.
- [ ] 4.4 Add acquisition waveform marker rendering with point and interval states.
- [ ] 4.5 Add integration tests proving an event-write failure does not stop raw acquisition.

## 5. Review integration

- [x] 5.1 Load RecordingEvents alongside the existing review session without reading the complete raw recording.
- [ ] 5.2 Add shared event timeline overlay and event list.
- [x] 5.3 Add seek-to-event while preserving the current montage, filter, cache, and playback contracts.
- [ ] 5.4 Add allowed manual create/edit/delete behavior and read-only external/system behavior.

## 6. External sources and documentation

- [ ] 6.1 Define adapters for device Trigger and imported Annotation without changing raw channel semantics.
- [x] 6.2 Document event data ownership, time units, source meanings, and historical snapshots (`DefinitionCodeSnapshot`, `DefinitionNameSnapshot`, `ColorSnapshot`, definition version).
- [x] 6.3 Add regression tests proving a disabled or deletion-restricted definition does not prevent existing RecordingEvents from loading or displaying.
- [ ] 6.4 Run desktop tests, `openspec validate add-event-annotation-system --strict --no-interactive`, and `git diff --check`.
- [ ] 6.5 Produce a validation report before archive; archive only after every task and post-archive strict validation pass.
