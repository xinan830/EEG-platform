# Proposal

## Why

Temporarily unloading the Review dock host closes its floating analysis windows while retaining each slot's floating state. When the host loads again, the windows are not recreated, leaving the UI state and visible panels inconsistent.

## What Changes

- Restore floating analysis windows from the current slot collection whenever the dock host loads.
- Preserve existing cleanup on unload and avoid creating duplicate windows during repeated load notifications.
- Record the current file-size assessment for the Review composition and dock host.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/review-algorithm-link`: floating analysis panels remain available when the Review host is temporarily unloaded and loaded again.

## Impact

- Affected code: `MultiAnalysisDockHost`, its Review desktop lifecycle tests, and `docs/code-size-policy.json`.
- Compatibility: no public API, stored data, or user configuration changes; existing floating slots are restored using their current state.
- Non-goals: persist window geometry across application restarts, change panel docking behavior, or modify EEG calculations.
- Scientific-contract impact: none. Time, units, channel ordering, analysis inputs, and API/WebSocket messages are unchanged.
- Migration/rollback: no migration is required. Reverting the load synchronization restores the prior lifecycle behavior.
