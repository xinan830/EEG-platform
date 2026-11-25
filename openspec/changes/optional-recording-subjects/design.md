# Design

## Context
Sessions currently require subjects; local WPF records use immutable manifests and mutable recording-display.json. Subject names are absent from models. See proposal.md for motivation.

## Goals / Non-Goals
Unify optional entry and presentation without changing acquisition stream behavior or scientific source identity. No subject management application, header mutation or automatic matching by names.

## Decisions
- Migration 25 rebuilds subjects with nullable unique local_code and a name column, and sessions with nullable subject_id, preserving all existing IDs, rows, indexes and foreign keys. No placeholder identities.
- A shared backend resolver inside the caller transaction validates project ownership, handles existing ID/code or creates a name-only subject, and returns null when metadata is absent. Existing code resolves one subject; explicit name edits update that subject and the editor warns that other records sharing this code reflect the name change.
- Expand the existing typed Session PATCH to atomically update display name and optional subject metadata; omit both fields to preserve the association, submit both blank to clear it, and reject a partially submitted pair with 422. Existing rename clients remain valid.
- Local records persist optional metadata in recording-display.json through one typed store reused by acquisition and edit/list. Preserve existing displayName and unrelated JSON fields when changing subjects. Seed metadata at raw writer creation from project context so preview creates no files. This does not change raw sample manifests or scientific identities.
- WPF adds code/name fields to preparation and editor, and optional selection/new entry to import. List rows display one compact subject column with trimming and tooltip. Backend sessions join subject details for display, so shared edits do not leave stale names.
- Import subject validation remains atomic with file publication and recording/Session insertion; failures clean up staged/published files.

## Risks / Trade-offs
- Shared code name edits affect multiple imported records: explain inline and refresh all visible imported rows. Local record metadata remains record-owned because no backend subject identity exists for WPF acquisition.
- Names are personal metadata: user-entered only; no names in logs, generated IDs, filenames or algorithm payloads.
- Migration rollback requires restoring a database backup; do not delete or rewrite existing recordings. Test upgrading populated databases and foreign_key_check.

## Migration Plan
Deploy backend and desktop together. Run additive/rebuild migration transactionally on startup. Old calls with subject IDs remain accepted; omitted subject fields in PATCH retain the old subject. Existing local records without metadata display 未关联.
