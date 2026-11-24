# Remove Analysis Time Hard Limits

## Why

The official Run path accepts analysis ranges longer than 120 seconds, while
the legacy spectrum and spectrogram GET endpoints still reject them at the API
boundary. Some algorithm parameter schemas also advertise an unrelated
120-second dynamic-window maximum. This creates contradictory client contracts.

## What Changes

- Remove the legacy GET endpoint's fixed 120-second query constraint.
- Enforce the true recording-duration boundary in the shared spectral service;
  out-of-range requests fail instead of being silently truncated.
- Remove the obsolete `maximum=120` metadata from dynamic-window parameter
  schemas. Supported window values remain owned by each manifest's
  `dynamic_policy`.
- Remove the workspace's fixed 30-second Run-polling cutoff so a queued or
  long-running official Run keeps updating until it reaches a terminal state.
- Align user-facing algorithm-contract documentation with these rules.

## Impact

This changes request validation, Run polling, and documentation only. It does not change
signal processing, quality gates, dynamic-window policies, or scientific
algorithm versions.

## Compatibility

Existing in-bounds requests retain their behavior. Previously rejected legacy
requests longer than 120 seconds become valid when the recording contains the
complete requested interval. Requests extending past a recording end now fail
explicitly rather than receiving a shorter, incorrectly represented result.

## Non-goals

- Do not remove the live-acquisition display filter's bounded pre-roll cache.
- Do not make dynamic-window values arbitrary; algorithm manifests remain the
  authority for supported values.
- Do not change Welch, filtering, quality-gate, or scientific version rules.

## Migration

No client payload migration is required. Clients should use the recording
duration to bound user input and surface the existing structured range error
when a user asks for data beyond the recording.
