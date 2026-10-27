## Why

Startup currently fails when the durable montage reference index contains an empty identity set for a recording path whose manifest now contains a valid montage ID and matching snapshot ID. This blocks all montage and channel configuration refresh even though the manifest itself is internally consistent.

## What Changes

- Reconcile an empty per-recording identity entry from the verified manifest during reference discovery.
- Continue rejecting changes when a previously recorded non-empty identity set differs from the manifest.
- Keep reference index verification fail-closed for malformed manifests, inaccessible locations, and non-empty identity conflicts.

## Capabilities

### New Capabilities

- `desktop/montage-reference-reconciliation`: Verify and recover empty path entries in the historical montage reference index.

### Modified Capabilities

None.

## Impact

- Affected code: desktop montage reference index and lifecycle tests.
- APIs, dependencies, raw recording format, and scientific behavior: unchanged.
- Compatibility: existing version-2 indices are reconciled in place on the next successful scan; no destructive migration or raw-file edit is required.
- Non-goals: rewriting manifests, relaxing mismatches between non-empty identities, or permitting edits/deletion when reference verification fails.
