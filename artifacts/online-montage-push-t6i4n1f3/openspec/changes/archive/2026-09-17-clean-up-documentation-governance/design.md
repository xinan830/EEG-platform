## Context

OpenSpec already contains current specifications and 22 completed changes. The `docs/changes/` and `docs/superpowers/` trees contain earlier, overlapping implementation notes. Retaining them in the normal documentation tree implies they may define current behavior, even when later runtime changes supersede them.

## Decision

Documentation has four stable roles:

1. `openspec/specs/` defines current observable contracts.
2. `openspec/changes/archive/` records completed change decisions and acceptance work.
3. `docs/architecture/` and `docs/algorithms/` explain current implementation and scientific context for people.
4. `docs/validation/` records reproducibility and verification evidence.

Historical ad-hoc change notes move to `docs/archive/change-notes/` with an explicit non-authoritative index. The `docs/superpowers/` drafts are removed because they are not referenced outside that tree and their completed work is represented by OpenSpec archives. Git history remains the recovery path for any removed draft.

## Alternatives

- Keep every file in place: rejected because it preserves multiple apparent sources of truth.
- Delete all historical notes: rejected because change notes provide useful debugging context.
- Promote every draft into a current specification: rejected because plans contain superseded implementation detail and would make contracts unreliable.

## Migration And Rollback

Moves are recorded by Git. A linkable archive index explains that archived notes are historical context only. If a removed `superpowers` draft proves uniquely necessary, it can be restored from Git history without changing scientific data or public APIs.

## Validation

- Confirm no external file refers to `docs/superpowers/`.
- Confirm the moved history is present under `docs/archive/change-notes/`.
- Confirm OpenSpec strict validation before and after archive.
- Run `git diff --check`.
