## Context

The desktop currently polls a foreground full Run until terminal status. A configuration change asynchronously requests backend cancellation, but the polling loop remains part of `isSubmittingRun`; the automatic-run debounce rejects the newest strategy while that old task remains pending. The Run creation request itself also has a gap before its ID reaches `LastRun`, so invalidation may happen before there is an ID to cancel.

## Goals / Non-Goals

**Goals:** One shared owner handles foreground execution generations, cancellation, polling lifetime, and stale-response isolation across all official algorithms. The newest configuration can start after the previous Run is cancelled or its ID becomes available. Combined-mode background full ownership remains separate.

**Non-goals:** Force-kill NumPy/SciPy/native execution, preempt a running window, change scientific algorithms or parallelism, or cancel an explicitly requested combined background Run when only a preview target changes.

## Decisions

- Extend the shared `AlgorithmRunCoordinator` with a replaceable foreground execution lifetime instead of implementing handoff logic in each result handler or algorithm.
- A foreground operation carries its configuration generation and cancellation token. Invalidation cancels the previous client wait and requests backend cancellation for a known active Run ID. If creation returns after invalidation, the coordinator immediately cancels that returned ID and never publishes it as current.
- Cancellation of the client wait is distinct from backend cancellation. The former releases the UI's single-submission gate promptly; the latter marks durable backend work and is observed between scientific windows.
- Automatic configuration changes use the same invalidation path. Debounce remains for coalescing rapid changes, but it cannot wait for stale polling to finish.
- Combined background full work retains its existing dedicated owner and cancellation policy. A preview cursor change does not invalidate its scientific identity.

## Risks / Trade-offs

- A Run may complete just before its cancel request. Treat `RUN_NOT_CANCELLABLE` as a status refresh and rely on generation checks to keep its result out of the current view.
- A currently executing scientific window can retain the shared compute slot until its safe boundary; prompt UI handoff does not promise an immediate new numeric value.
- Rapid configuration changes can produce multiple backend Run records, but obsolete ones are cancellation-requested and remain traceable rather than being hidden or deleted.

## Migration Plan

No API or database migration is needed. Roll out the WPF coordinator and its tests together; existing backend cancellation and window-boundary checks are reused.

## Open Questions

None.
