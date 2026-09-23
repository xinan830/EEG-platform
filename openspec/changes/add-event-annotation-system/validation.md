# Event Annotation System Validation Report

## Scope

This report covers the desktop event-definition catalog, independent recording
event metadata, acquisition/review controls, event rendering, and local-store
behavior implemented by `add-event-annotation-system`.

## Automated Validation

Executed on 2026-09-23:

```text
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore
Result: 216 passed, 0 failed, 0 skipped

dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj --no-restore
Result: 0 warnings, 0 errors

openspec validate add-event-annotation-system --strict --no-interactive
Result: valid

git diff --check
Result: passed
```

The test coverage includes definition Code and shortcut conflicts, definition
snapshots, point and interval events, restart round-trip persistence, corrupt
event-file non-overwrite behavior, externally sourced read-only events,
definition filtering and deletion restrictions, non-zero counter coordinate
mapping, and raw-writer completion after a failed/cancelled event write.

## Scientific Time Semantics

- `RecordingEvent.StartSample` and `DurationSamples` are Recording-relative
  sample coordinates. Seconds are derived with the manifest sampling rate.
- Acquisition captures the latest device counter before asynchronous event
  persistence, subtracts the Recording's first device counter, and rejects a
  negative result. This avoids using PC wall-clock time and prevents a counter
  reset from being silently represented as continuous time.
- Counter gaps are emitted to `audit.jsonl`; review preserves them as empty
  waveform ranges rather than inserting samples.
- Event metadata is kept in `events.json`, separate from immutable raw chunks.

## Remaining Hardware Acceptance

This change is **not archived**. The only remaining OpenSpec task is `1.3`:
validate sample-counter behavior when the physical ANT/eego device disconnects,
reconnects, or resets its counter.

The current runtime safely faults on counter rollback/out-of-order data instead
of silently joining unrelated counter segments. That is a safe failure mode,
but it is not evidence that a production reconnect policy has been accepted.
To close the task, capture and retain target-device evidence for:

1. initial non-zero counter and manual event alignment in review;
2. a detected gap and its audit/review representation;
3. USB/device disconnect during recording;
4. reconnect with both continued and reset counter behavior;
5. completed raw recording independently parsed from its manifest and audit.

Only after those results define an accepted reconnect policy can the task be
checked, strict validation rerun, and this change archived.
