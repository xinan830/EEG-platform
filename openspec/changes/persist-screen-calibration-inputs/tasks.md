## 1. Persistence contract

- [x] 1.1 Add exact-match and latest-valid fallback loading while preserving the existing JSON profile format.
- [x] 1.2 Make calibration writes atomic and keep the previous file on write failure.

## 2. WPF behavior

- [x] 2.1 Persist a valid width/height pair after input changes or explicit save, without persisting invalid drafts.
- [x] 2.2 Reload the restored profile when the final display context is attached and notify conversion bindings.

## 3. Verification

- [x] 3.1 Add tests for ViewModel restart round-trip, invalid draft preservation, fallback loading, and atomic store behavior.
- [x] 3.2 Run desktop tests, OpenSpec strict validation, and `git diff --check`.
