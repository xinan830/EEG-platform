## Why

Deleting dynamic-window files without removing their SQLite records leaves READY windows pointing at missing JSON/NPZ files. Operators need a supported cleanup action in Settings instead of manually deleting directories or editing SQLite.

## What Changes

- Add a Settings cache section with one asynchronous clear action and an observable completion/error result.
- Clear the connected backend's dynamic-window sessions, windows, attempts and subscriptions together with their derived artifacts and read buffers.
- Clear this desktop user's review-cache and review-display-cache while excluding active recording-access sessions.
- Refuse cleanup while storage/analysis requests or analysis work are active; exclude recovery and new storage/analysis requests during cleanup. Independent live-filter and live-recording batch APIs remain available.
- Preserve recordings, historical Runs/artifacts, audit records, configuration and database schema/index definitions.

## Capabilities

### New Capabilities
- `desktop/cache-maintenance`: safe, user-triggered cleanup of rebuildable playback caches.

### Modified Capabilities
None.

## Impact

New current API: POST /api/maintenance/cache/clear, consumed by WPF Settings. No schema migration or scientific-contract change: sample ranges, units, channel order, quality and provenance remain unchanged. No automatic cleanup, alternate database discovery, historical-result removal, preprocessing rewrite or lag fix. Only the connected backend is cleaned; an inactive installation's database is not edited. Rollback is code rollback; cleared derived windows are regenerated from preserved sources.
