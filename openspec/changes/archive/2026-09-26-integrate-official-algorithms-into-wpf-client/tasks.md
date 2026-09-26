## 1. Contract and registration

- [x] 1.1 Define and implement the backend completed-WPF-recording registration request/response and structured errors.
- [x] 1.2 Add backend validation for manifest identity, chunk completeness, channel order, units, sample rate, gaps, and idempotency.
- [x] 1.3 Add registration API and backend contract tests, including incomplete/corrupt recording rejection.
- [x] 1.4 Add the WPF registration DTO/client and invoke it only from the completed-recording workflow.

## 2. WPF algorithm transport

- [x] 2.1 Add typed WPF clients for `/api/algorithms`, `/api/runs`, artifacts, and structured preview.
- [x] 2.2 Map the initial PSD/static configuration to a typed WPF submission without duplicating scientific validation.
- [x] 2.3 Add bounded Run polling and terminal-state/error-code mapping.

## 3. WPF product workspace

- [x] 3.1 Replace the algorithm-list placeholder with a WPF algorithm list and availability states.
- [x] 3.2 Add the first static Run workflow from a completed WPF recording directory.
- [x] 3.3 Add scalar result and provenance display.
- [x] 3.4 Add backend-owned PSD/STFT structured preview display with explicit axes, units, and unavailable cells.

## 4. Verification and closeout

- [x] 4.1 Add WPF unit/contract tests for registration, catalog, Run polling, terminal errors, and null handling.
- [x] 4.2 Run backend tests and WPF build/test; run OpenSpec strict validation and `git diff --check`.
- [x] 4.3 Complete a manual WPF static PSD round trip against a completed local recording.
- [x] 4.4 Update architecture documentation to state WPF product ownership and Vue validation-only status.
- [x] 4.5 Record validation evidence and archive this OpenSpec only after all tasks pass.
