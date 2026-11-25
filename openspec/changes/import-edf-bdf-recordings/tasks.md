# Tasks

## 1. Contracts and project identity

- [x] 1.1 Add backend tests for resolving a desktop project's stable ID to one backend Project and rejecting cross-project Subject/Session references; verify duplicate imports do not create duplicate Project rows.
- [x] 1.2 Add typed desktop contracts and client methods for project resolution, subject listing/creation, and Session listing/creation; verify JSON names, required fields, and structured error handling with client tests.
- [x] 1.3 Implement idempotent backend project resolution using the desktop project ID and add any required additive migration; verify startup migration is repeatable and existing project rows remain unchanged.

## 2. Bounded EDF/BDF import and Session publication

- [x] 2.1 Add backend tests for streamed staging, SHA-256 and byte-length integrity, source-order metadata, channel type/unit alignment, malformed EDF/BDF rejection, cancellation, disconnect, and disk-write failure cleanup.
- [x] 2.2 Implement bounded, cancellable upload staging and metadata validation for project imports while preserving the standalone import request/response contract; verify focused recording API and service tests pass.
- [x] 2.3 Implement transactional project-scoped recording, optional subject creation by local code, and Session association; verify success creates exactly one recording and Session, while every failure leaves no partial recording or Session association.

## 3. Desktop project import flow

- [x] 3.1 Add ViewModel tests for file selection results, subject selection/creation, transfer progress, cancellation, structured failure, and refresh of only the selected project's recording list.
- [x] 3.2 Add a project-level EDF/BDF import action and dialog that displays validated file facts and collects an existing or new non-identifying subject code; verify the complete request is scoped to the selected project.
- [x] 3.3 Implement cancellable streamed desktop upload and completion handling; verify cancellation stops transfer and failed imports do not appear in the project list.
- [x] 3.4 Merge backend imported Session rows with local WPF recordings without changing WPF identity or filtering; verify imported rows retain recording ID, Session ID, project context, and source kind across refresh.

## 4. EDF/BDF review access

- [x] 4.1 Add backend tests and a typed bounded raw-sample-range endpoint for EDF/BDF; verify file-relative sample bounds, V/float64 values, source channel order, explicit discontinuities, and structured unavailable/corrupt errors.
- [x] 4.2 Add a desktop EDF/BDF access adapter for the existing review source contract and update derived-cache identity to include recording hash, sample rate, ordered channel schema, and filter contract; verify read-window and cache tests pass.
- [x] 4.3 Route imported project rows to the shared review workspace and offline analysis; verify existing chart/UI styles and algorithm execution remain the same while WPF recording navigation tests still pass.

## 5. Integration and validation

- [x] 5.1 Verify project return navigation, imported row refresh, cancellation, corrupt-file handling, and concurrent import behavior with focused backend and desktop integration tests.
- [x] 5.2 Run OpenSpec validation and all focused backend/desktop tests; record any environment-limited checks and inspect changed business-file sizes, adding code-size policy reviews where required.
- [x] 5.3 Update the current user-facing import/review documentation and verify documented import, cancellation, and review behavior matches the implementation.
- [x] 5.4 Support deletion of imported project Session links with guarded source cleanup, and restore export-sidecar identity when disabled inputs have no electrode label; verify referenced sources are retained and round-trip mappings stay aligned.

## 6. Imported provenance and offline algorithm execution

- [x] 6.1 Add contract tests for complete sidecar round-trip: hash, channel configuration, source-column/native-index mapping, acquisition montage, and montage configuration; verify absent sidecar is treated as plain EDF/BDF and malformed present sidecar rejects atomically.
- [x] 6.2 Persist and return the validated montage snapshots with imported recording metadata using an additive migration; preserve source column order and do not rewrite raw signal bytes.
- [x] 6.3 Replace the review algorithm panel's WPF-directory-only registration assumption with a source-neutral typed descriptor; construct imported descriptors from backend recording metadata and keep the backend recording ID as execution identity.
- [x] 6.4 Resolve imported EEG channels and compatible montage inputs from validated snapshots, excluding non-EEG/disabled inputs according to the central channel projection; return structured unavailable reasons when requirements cannot be met.
- [x] 6.5 Add desktop/backend integration tests proving an imported recording can request and render a supported algorithm window, with channel order, units, absolute sample bounds, source hash, and montage identity validated end to end.
- [x] 6.6 Run focused backend and desktop test suites plus OpenSpec validation; inspect changed business-file sizes and document any remaining environment limitations.
