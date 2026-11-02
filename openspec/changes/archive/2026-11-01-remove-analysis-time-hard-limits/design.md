# Design: Remove Analysis Time Hard Limits

## Decision

The recording duration is the sole upper boundary for an offline static
analysis interval. The API validates a request from `start_s` through
`start_s + window_s` before loading samples. It no longer clamps a request to
the recording end, because that would make the returned result represent a
different interval from the one the caller submitted.

Dynamic windows use a distinct contract. Their permitted values are supplied
by each runnable algorithm's `DynamicAnalysisPolicy`, while the selected
analysis range must still remain in the recording. Parameter schemas therefore
must not advertise a generic `maximum=120` that disagrees with that policy.

The official Run workspace polls until the backend returns a terminal state;
there is no arbitrary client-side attempt count. This prevents a long queued
or long-running run from appearing to fail merely because the browser waited
for 30 seconds.

## Alternatives Considered

- **Keep the 120-second API cap:** rejected because it conflicts with the
  official Run path and with long recordings.
- **Remove all time limits:** rejected because a request beyond recording data
  cannot have a scientifically valid actual range.
- **Clamp to the recording end:** rejected because it silently changes the
  requested evidence range and weakens provenance.

## Migration And Rollback

The endpoints retain their paths, fields, and minimum four-second rule. A
rollback can restore the former query constraint, but would reintroduce a
contradictory path and is not recommended. No persisted data is transformed.

## Storage Ownership

No new storage is introduced. Existing run provenance continues to own
requested and actual time ranges; legacy responses retain their established
`window_start_s` and `window_duration_s` fields.

## Test Strategy

Cover a long in-bounds PSD service request, an out-of-range service request,
and both legacy GET endpoints above 120 seconds. Verify every built-in
algorithm's dynamic-window parameter metadata has no legacy global maximum.
