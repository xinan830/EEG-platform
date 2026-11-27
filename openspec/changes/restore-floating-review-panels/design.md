# Design

## Context

See [proposal.md](proposal.md) for the problem and scope. The slot collection is the durable in-session source of truth; the floating window manager owns disposable WPF windows and `Sync` is idempotent for slots that already have windows.

## Goals / Non-Goals

**Goals:** Reconcile visible floating windows with the current floating slot collection whenever the dock host enters the visual tree.

**Non-Goals:** Persist window geometry across process restarts or change docking, analysis, or scientific behavior.

## Decisions

- Keep closing owned windows on `Unloaded` so temporary visual-tree removal cannot leave orphaned windows.
- Invoke the existing `SyncFloatingWindows` method on `Loaded`. It materializes the current slot collection and reuses manager idempotency, so retained slots reopen and existing windows do not duplicate.
- Test with an actual owner `Window`, detach and reattach the host, and assert slot retention, one owned window, and recreation after unload.
- Keep the implementation in the current dock host. It already owns floating slot collection lifecycle; no separate lifecycle abstraction is needed.

## Risks / Trade-offs

- [Risk] A loaded notification could synchronize more than once → `MultiAnalysisFloatingWindowManager.Sync` skips slots already present in its window map.
- [Risk] The existing Review composition has crossed its registered line limit → update its size-policy rationale and keep a concrete view-extraction follow-up; register the dock host's current single-owner lifecycle responsibility separately.

## Migration Plan

No migration or persistent state is involved. Rollback consists of removing the load synchronization; unload cleanup remains unchanged.
