# Tasks

## 5. Optional demographics
- [x] 5.1 Add nullable sex and recording-age contracts, additive migration and atomic import/edit persistence; verify optionality, strict validation, migration and age independence.
- [x] 5.2 Extend the existing shared selector, typed client and local metadata so acquisition/import/edit preserve sex and per-record age without affecting scientific files.
- [x] 5.3 Add focused backend/desktop regressions, run relevant suites and Debug build, update documentation and size assessments, and validate OpenSpec/diff.

## Demographics validation evidence (2026-11-25)
- Backend: 80 passed across demographics, subjects, imports, project/session APIs, record names, research primitives and three migration modules. Covers populated v25 upgrade, nullable defaults, validation/rollback, omitted versus null age/sex and independent historical ages; corrected the older-schema test fixture to remove new columns before reconstructing v10/v11.
- Desktop: 63 passed across shared selector/directory, import and HTTP contracts, metadata storage, record editing/navigation, backend access, acquisition writer/coordinator and project filtering. WPF initialization verifies sex/age bindings; selecting a person never copies another record's age, clearing the legacy subject fields preserves record age, and raw samples/manifests stay unchanged.
- Debug application build succeeded in `.tmp/subject-directory` with zero warnings/errors; the running client was left untouched. Two pre-existing test analyzer warnings and three backend test-library/date-fixture warnings remain.
- Updated architecture and oversized-file responsibility assessments; strict OpenSpec validation and repository-native diff whitespace checks passed. New fields are management metadata only; source/scientific and WebSocket contracts remain unchanged by this feature.
- No manual UI/hardware operation, no default export of demographics, and no Git commit/push. Restart the backend for migration 26 and rebuild/reopen the desktop client to load the new interface. Unrelated ongoing preprocessing/artifact changes are preserved.

## 1. Backend optional association
- [x] 1.1 Migrate nullable subjects and names preserving IDs; verify populated upgrade and foreign keys.
- [x] 1.2 Share subject resolution across import and Session editing; verify empty, name-only, code-only, existing, clear and cross-project cases with API tests.

## 2. Desktop entry and persistence
- [x] 2.1 Extend typed client and import dialog for optional subject fields; verify request serialization and import tests.
- [x] 2.2 Persist acquisition subject metadata using shared local store; verify preparation-to-writer and metadata preservation tests.
- [x] 2.3 Extend record editor and subject list column for local/imported recordings; verify edit/reload and compilation.

## 3. Integration
- [x] 3.1 Update architecture and size assessments, run related backend/desktop regressions, OpenSpec strict validation and diff checks; report limits accurately.

## Validation evidence (2026-11-25)
- Backend: 93 passed across subject, import, project, migration, recording access and scientific preprocessing/RBP regressions; after paired-field validation, all 19 subject/project/name tests passed again.
- Desktop: 49 passed covering import HTTP/client behavior, XAML dialog initialization, local metadata failure/cancellation, record editing/reload, acquisition writer/coordinator, backend access and project filtering.
- Normal Debug desktop build succeeded with zero warnings/errors. Test build retained two pre-existing analyzer warnings in unrelated algorithm tests.
- OpenSpec strict validation, changed-business-file size assessments and git diff --check passed.
- No physical amplifier or manual UI operation performed; no Git commit/push requested. EDF/BDF subject-header prefill and metadata export are outside this change.

## 4. Consistent selection and subject management
- [x] 4.1 Add project-scoped subject resolve/update/delete APIs and explicit Session subject-ID binding; test ownership, duplicates, detach and unchanged signal identities.
- [x] 4.2 Bind local metadata to stable subject IDs and reuse the shared directory for list projection; test legacy binding and rename/delete propagation.
- [x] 4.3 Reuse one selector in import, acquisition and editing with edit/delete confirmation; test existing/name-only/new/empty flows and UI initialization.
- [x] 4.4 Run related backend/desktop regressions, Debug build, size/diff and strict OpenSpec checks; document evidence and remaining limits.

## Shared selector validation evidence (2026-11-25)
- Backend: 50 passed across recording subjects, EDF/BDF imports, project APIs, record names and research primitives. Added stable-ID rename, duplicate/cross-project rejection, explicit null/omitted Session binding, name-only isolation and subject-delete detachment tests; source metadata and bytes remain unchanged.
- Desktop: 54 passed across SubjectDirectoryTests, ProjectRecordingImportTests, HttpResearchProjectClientTests, RecordingSubjectMetadataTests, ProjectRecordingReviewNavigationTests, BackendRecordingAccessSessionTests, LocalAcquisitionRawWriterTests, AcquisitionCoordinatorTests and ProjectWorkspaceFilteringTests. Covers legacy local binding, name-only editor migration, reuse without duplicates, rename/delete propagation, failed loads, project changes, typed HTTP and WPF binding initialization.
- Debug application build succeeded with zero warnings/errors in `.tmp/subject-directory`. Default output could not replace the apphost because the running BrainPlatform.Desktop process held it open; the process was left running. Two existing analyzer warnings remain in unrelated desktop algorithm tests, plus existing backend test-library/date-fixture warnings.
- Changed source size assessments, strict OpenSpec validation and repository-native `git diff --check` passed. Existing large composition files keep their registered extraction plans; new selector/directory owners remain below 400 lines.
- No desktop control, physical amplifier verification or Git commit/push performed. Restart the backend and close/rebuild/reopen the existing client to load the new APIs and UI. This change leaves scientific algorithms, preprocessing, channels, montages and raw signal contracts untouched; unrelated in-progress bad-channel work is preserved.
