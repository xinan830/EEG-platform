## 1. Contract and regression coverage
- [x] 1.1 Add typed backend/desktop cleanup contracts and tests for preservation, missing artifacts, idle/busy/concurrent requests and partial failures.

## 2. Backend maintenance
- [x] 2.1 Add the request/recovery exclusion boundary, transactional dynamic-record reset and fixed artifact cleanup with read-cache/accounting invalidation.
- [x] 2.2 Register the maintenance endpoint and audit the operation without changing scientific calculation.

## 3. Desktop Settings
- [x] 3.1 Add recording-access exclusion, local cache cleaner and HTTP maintenance client.
- [x] 3.2 Add the Settings ViewModel and compact cache section with progress and actionable completion/error text.

## 4. Verification
- [x] 4.1 Run backend regression tests, desktop tests/build, size and diff checks, and strict OpenSpec validation.
- [x] 4.2 Record automated evidence and identify any outstanding GUI acceptance.

Evidence and existing global size-check exception: `docs/validation/2026-11-07-settings-cache-cleanup.md`.
