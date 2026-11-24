## Why

Registering a validated WPF recording from a new directory can find its existing database row by content hash, then fail because the row still points to a removed directory. Rebinding the row before resolving its montage catalogue lets moved recordings reopen without changing their content identity or snapshots.

## What Changes

- When a content-identical WPF recording is registered from a new directory and the stored directory is missing, update the recording's source directory before loading its catalogue.
- Preserve the recording identifier and database-bound montage snapshots during relocation.
- Keep existing behavior when the stored source directory still exists.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `data/recordings`: Define registration behavior for content-identical WPF recordings whose prior directory is unavailable.

## Impact

- Backend registration service and recording-registration tests.
- No public API shape, EEG sample values, time basis, channel ordering, units, or scientific calculation changes.
- No schema migration; existing `stored_name` is updated only after the submitted recording validates and its content hash matches.
