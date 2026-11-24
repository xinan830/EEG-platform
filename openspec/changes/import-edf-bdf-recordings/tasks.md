# Tasks

## 1. Contracts and project identity

- [ ] 1.1 Add backend tests for resolving a desktop project's stable ID to one backend Project and rejecting cross-project Subject/Session references; verify duplicate imports do not create duplicate Project rows.
- [ ] 1.2 Add typed desktop contracts and client methods for project resolution, subject listing/creation, and Session listing/creation; verify JSON names, required fields, and structured error handling with client tests.
- [ ] 1.3 Implement idempotent backend project resolution using the desktop project ID and add any required additive migration; verify startup migration is repeatable and existing project rows remain unchanged.

## 2. Bounded EDF/BDF import and Session publication

- [ ] 2.1 Add backend tests for streamed staging, SHA-256 and byte-length integrity, source-order metadata, channel type/unit alignment, malformed EDF/BDF rejection, cancellation, disconnect, and disk-write failure cleanup.
- [ ] 2.2 Implement bounded, cancellable upload staging and metadata validation for project imports while preserving the standalone import request/response contract; verify focused recording API and service tests pass.
- [ ] 2.3 Implement transactional project-scoped recording, optional subject creation by local code, and Session association; verify success creates exactly one recording and Session, while every failure leaves no partial recording or Session association.

## 3. Desktop project import flow

- [ ] 3.1 Add ViewModel tests for file selection results, subject selection/creation, transfer progress, cancellation, structured failure, and refresh of only the selected project's recording list.
- [ ] 3.2 Add a project-level EDF/BDF import action and dialog that displays validated file facts and collects an existing or new non-identifying subject code; verify the complete request is scoped to the selected project.
- [ ] 3.3 Implement cancellable streamed desktop upload and completion handling; verify cancellation stops transfer and failed imports do not appear in the project list.
- [ ] 3.4 Merge backend imported Session rows with local WPF recordings without changing WPF identity or filtering; verify imported rows retain recording ID, Session ID, project context, and source kind across refresh.

## 4. EDF/BDF review access

- [ ] 4.1 Add backend tests and a typed bounded raw-sample-range endpoint for EDF/BDF; verify file-relative sample bounds, V/float64 values, source channel order, explicit discontinuities, and structured unavailable/corrupt errors.
- [ ] 4.2 Add a desktop EDF/BDF access adapter for the existing review source contract and update derived-cache identity to include recording hash, sample rate, ordered channel schema, and filter contract; verify read-window and cache tests pass.
- [ ] 4.3 Route imported project rows to the shared review workspace and offline analysis; verify existing chart/UI styles and algorithm execution remain the same while WPF recording navigation tests still pass.

## 5. Integration and validation

- [ ] 5.1 Verify project return navigation, imported row refresh, cancellation, corrupt-file handling, and concurrent import behavior with focused backend and desktop integration tests.
- [ ] 5.2 Run OpenSpec validation and all focused backend/desktop tests; record any environment-limited checks and inspect changed business-file sizes, adding code-size policy reviews where required.
- [ ] 5.3 Update the current user-facing import/review documentation and verify documented import, cancellation, and review behavior matches the implementation.
