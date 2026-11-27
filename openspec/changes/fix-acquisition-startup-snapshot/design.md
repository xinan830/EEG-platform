# Design

## Context
See proposal.md. The workspace already owns IsPreparationStartInProgress; display preferences own delayed filter tasks, and the workflow owns device configuration and persistence. Keep these owners.

## Goals / Non-Goals
Cover every await in startup without adding another state machine, manager or DSP implementation. Existing raw sample, channel and API contracts remain unchanged.

## Decisions
- Display preferences consult the workspace's existing preparation flag. Guard setters as well as control availability; disabling controls alone does not protect programmatic edits.
- Track the last pending filter task. At startup invalidate its revision and await completion before applying the snapshot, including a worker already inside runtime configuration. Checking a revision only before Task.Run is insufficient.
- Recheck the captured input after device configuration and settings persistence and immediately before stream opening. Continue consuming prepared values rather than mutable UI fields.
- Use deterministic completion gates for discovery and settings saves. Exercise both successful startup and failure/retry.
- Retain the current responsibilities and size-policy exceptions; update measured counts only.

## Risks / Trade-offs
- Startup can wait for an already-running display update → it must complete before startup can safely configure the shared bridge. No UI blocking wait is introduced.
- Settings may already have saved the validated snapshot before a non-filter edit is detected → stream opening is rejected explicitly; no raw recording is created.

## Migration Plan
No migration or new package. Revert the focused code and tests to roll back; do not reset unrelated workspace edits. Release tests and backend gate provide engineering verification, not hardware acceptance.
