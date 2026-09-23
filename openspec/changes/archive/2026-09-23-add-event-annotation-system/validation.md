# Event Annotation System Validation Report

## Scope

This report covers the desktop event-definition catalog, independent recording
event metadata, acquisition/review controls, event rendering, and local-store
behavior implemented by `add-event-annotation-system`.

## Automated Validation

Executed on 2026-09-23:

```text
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore
Result: 221 passed, 0 failed, 0 skipped

dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj --no-restore
Result: 0 warnings, 0 errors

openspec validate add-event-annotation-system --strict --no-interactive
Result: valid

git diff --check
Result: passed
```

The test coverage includes definition Code and shortcut conflicts, definition
snapshots, point and interval events, restart round-trip persistence, schema-0
envelope promotion on the next atomic write, corrupt event-file non-overwrite
behavior, externally sourced read-only events, definition filtering and deletion
restrictions, non-zero counter coordinate mapping, and raw-writer completion
after a real event-store filesystem write failure.

## Scientific Time Semantics

- `RecordingEvent.StartSample` and `DurationSamples` are Recording-relative
  sample coordinates. Seconds are derived with the manifest sampling rate.
- Acquisition captures the latest device counter before asynchronous event
  persistence, preserves it as `SourceSampleCounter`, then derives
  `StartSample` by subtracting the Recording's first device counter. Known gaps
  are not compressed. This avoids using PC wall-clock time and prevents a
  counter reset from being silently represented as continuous time.
- Events in a known gap can be persisted with `UnavailableGap` and
  `sample_counter_gap`; they remain traceable but are neither rendered nor
  seekable. A counter rollback is `UnavailableDiscontinuity`; live acquisition
  faults rather than silently continuing the current Recording, and records
  `SAMPLE_COUNTER_DISCONTINUITY` in `audit.jsonl`.
- Counter gaps are emitted to `audit.jsonl`; review preserves them as empty
  waveform ranges rather than inserting samples.
- Event metadata is kept in `events.json`, separate from immutable raw chunks.

## Target Device Acceptance

Manual acceptance was completed on the target ANT/eego environment on
2026-09-23. The operator verified manual event marking, acquisition, and
review playback, then interrupted the physical device connection. The desktop
did not silently continue the active Recording; device state was detected and
the overview recovered to normal device recognition after the connection was
restored.

The accepted reconnect policy for this change is deliberately conservative:
the interrupted Recording remains terminated rather than joining any later
counter stream. A future capability that continues one Recording across a
reconnect requires a separate design, physical counter evidence, and a
segmented raw-recording contract.
