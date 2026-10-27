## Why

Changing an algorithm, execution mode, or scientific configuration while a full Run is active requests backend cancellation, but the desktop keeps polling and treating that superseded Run as the active submission. The latest on-demand selection can therefore wait behind work the user has already replaced.

## What Changes

- Centralize supersession handling for desktop algorithm execution so every configuration-generation change cancels obsolete full Runs and releases their local polling wait.
- Let the latest selected mode start as soon as the old Run has a cancellable identity; prevent stale progress and results from updating the new configuration.
- Preserve combined mode semantics: its explicitly requested background full Run remains active when only its preview target moves.
- Document that backend cancellation takes effect at safe window boundaries; it does not forcibly interrupt a running NumPy/SciPy window.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/offline-analysis-execution-modes`: superseded execution must promptly hand off to the latest selected mode, including the safe cancellation boundary.
- `desktop/offline-analysis-orchestration`: configuration changes cancel obsolete foreground Runs while preserving explicitly owned combined-mode background Runs.

## Impact

- Affected code: WPF algorithm ViewModel execution coordination and desktop algorithm execution tests.
- API and persistence: no contract or schema changes; existing Run cancellation API is used.
- Compatibility: existing full, on-demand, and combined request formats remain unchanged.
- Scientific contract: no calculation, time, unit, channel-order, or provenance semantics change. Old-generation results remain historical and cannot be projected into the current view.
- Non-goals: forcibly terminating native scientific code, increasing compute parallelism, or cancelling a combined-mode background Run when only its preview target changes.
- Migration: none.
