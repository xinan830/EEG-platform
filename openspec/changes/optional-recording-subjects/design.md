# Design

## Context
Sessions currently require subjects; local WPF records use immutable manifests and mutable recording-display.json. Subject names are absent from models. See proposal.md for motivation.

## Goals / Non-Goals
Unify optional entry, project-scoped selection and basic subject edit/delete without changing acquisition stream behavior or scientific source identity. No separate subject management application, header mutation or automatic matching by names.

## Decisions
- Demographic extension: nullable subject `sex` uses `male`, `female`, `other` or null (未填写). Nullable `age_at_recording_years` is a whole-year recording/Session value from 0–150, not a current-age field or date-derived estimate. The shared WPF selector exposes sex for new/edit person and shows read-only sex for existing people; a separate field within the same control edits this recording's age in acquisition/import/record editing. Both remain optional; age alone does not create a placeholder subject. Sex without code/name is rejected instead of discarded.
- Additive migration 26 appends nullable subject sex and Session age with database checks; use explicit SQL insert columns. Old records remain null, omitted PATCH age preserves existing age and explicit null clears it. Subject edits/deletion preserve recording ages. Local cached person refresh must retain per-record age. Metadata never enters sample manifests, signal hashes, logs, algorithm requests or default EEG exports. Existing import publication stays atomic; new parameters are optional for current desktop clients.
- Migration 25 rebuilds subjects with nullable unique local_code and a name column, and sessions with nullable subject_id, preserving all existing IDs, rows, indexes and foreign keys. No placeholder identities.
- A shared backend resolver inside the caller transaction validates project ownership, handles existing ID/code or creates a name-only subject, and returns null when metadata is absent. Existing code resolves one subject; explicit name edits update that subject and the editor warns that other records sharing this code reflect the name change.
- Expand the existing typed Session PATCH to atomically update display name and optional subject metadata; omit both fields to preserve the association, submit both blank to clear it, and reject a partially submitted pair with 422. Existing rename clients remain valid.
- Local records persist optional metadata in recording-display.json through one typed store reused by acquisition and edit/list. Preserve existing displayName and unrelated JSON fields when changing subjects. Seed metadata at raw writer creation from project context so preview creates no files. This does not change raw sample manifests or scientific identities.
- WPF adds code/name fields to preparation and editor, and optional selection/new entry to import. List rows display one compact subject column with trimming and tooltip. Backend sessions join subject details for display, so shared edits do not leave stale names.
- Import subject validation remains atomic with file publication and recording/Session insertion; failures clean up staged/published files.
- Follow-up correction: one reusable WPF subject selector supplies unassociated/existing/new choices and edit/delete actions across all three entry points. Dedicated typed subject update/delete endpoints own directory mutations; backend resolve reuses the same transactional resolver as import. Session PATCH additionally accepts an explicit subject ID, mutually exclusive with code/name fields, preserving name-only identities.
- Local management metadata gains optional subjectId (also excluded from immutable manifests). Existing unbound local code/name metadata is explicitly resolved once when loading the project directory, preserving source bytes and display names. Once bound, the project subject directory is authoritative on refresh: rename follows the ID, deletion renders unassociated. Cached snapshots permit opening local records when the backend is unavailable; no live signal dependency is added.
- Delete requires the existing confirmation surface, clears Session associations transactionally and never deletes recordings. Subject deletion is unavailable from acquisition while a device session is active. Subject edits validate ownership and project-unique nonblank codes, and never log names.
- The selector owns loading, busy/editing state and cancellation; project IDs are checked after awaits. Local legacy binding is serialized within the project workspace and performed before managed rename/delete. Editor initialization re-reads the recording's stable identity after migration, including name-only subjects. Failed directory loads preserve cached associations and disable management rather than silently clearing metadata.

## Risks / Trade-offs
- Shared subject edits affect associated imported and local records; show an explicit notice and refresh local row projections from the shared directory. Local records retain a cached code/name snapshot for offline viewing but their stable subject ID controls online identity.
- Names are personal metadata: user-entered only; no names in logs, generated IDs, filenames or algorithm payloads.
- Migration rollback requires restoring a database backup; do not delete or rewrite existing recordings. Test upgrading populated databases and foreign_key_check.

## Migration Plan
Deploy backend and desktop together. Run additive/rebuild migration transactionally on startup. Old calls with subject IDs remain accepted; omitted subject fields in PATCH retain the old subject. Existing local records without metadata display 未关联.
