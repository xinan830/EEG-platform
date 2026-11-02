## Context

See `proposal.md` - Why. `inspect_wpf_recording` validates the submitted manifest, summary, sample chunks, channel units/order, and content hash before registration. Existing WPF rows can be found by content hash, but their `stored_name` also owns the source directory used to resolve montage snapshots.

## Goals / Non-Goals

**Goals:** Rebind an unavailable old directory to the already validated, content-identical submitted directory before calling `require_recording`.

**Non-Goals:** Reconcile different content, rewrite snapshots, change sample data, or move/copy source files.

## Decisions

- Rebind only when the content hash and import version match and the old directory is unavailable. Keep the current location if it still exists, and do not treat a path-only match with changed content as a relocation.
- Update `stored_name` and the display `original_name` in the same database operation, then call `require_recording` after the connection closes. Preserve the primary key and snapshot rows.
- Alternatives considered: create a duplicate row (breaks identity/snapshot continuity); blindly overwrite every matched path (moves a valid canonical source); delete snapshot metadata (loses user configuration history).

## Risks / Trade-offs

- [Risk] A stale path may become available again after a rebind → the database remains pointed at the last successfully validated directory; either directory contains the same content identity at rebind time.
- [Risk] A path-only row may refer to changed content → the relocation branch is restricted to exact content-hash and import-version matches.

## Migration Plan

No schema migration or source-file mutation is required. Rollback is a code revert; previously rebound rows continue to point to a valid copy of the same recording.
