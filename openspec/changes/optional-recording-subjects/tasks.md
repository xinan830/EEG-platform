# Tasks

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
