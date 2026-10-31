# Share waveform display frame publication identity

Review and TNK use independent waveform readers and payloads, but each implements its own stale-result publication checks. TNK validates the backend recording and montage, while Review tracks its own request revision and source revision. This leaves no shared contract proving that a completed frame belongs to the currently selected local recording source and view request.

This change adds one small request identity and publication gate shared by both consumers. It does not unify readers, move scientific work, change filtering, or alter waveform values.

## Scope

- Add a typed display request context containing the local recording ID, source revision, view generation, and request ID.
- Wrap complete reader payloads in that context before publication.
- Route asynchronous Review and TNK frame commits through one exact-identity and cancellation gate.
- Keep Review cache lookup and each reader's payload contract independent.

## Non-goals

- No change to playback time coordination or algorithm scheduling.
- No change to backend APIs, sample transport, filter parameters, units, channel ordering, or gap representation.
- No shared waveform cache or shared reader lifetime beyond the existing recording access manager.
