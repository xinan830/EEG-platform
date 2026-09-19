## 1. Implementation

- [x] Preserve the active filtered display buffer while a replacement revision
  is pending.
- [x] Build contiguous raw pre-roll plus the full visible display span and
  return only the fully filtered visible tail for replacement.
- [x] Gate candidate swaps and later append batches by filter configuration
  revision.

## 2. Verification

- [x] Extend contiguous-tail tests to assert visible-tail size.
- [x] Run backend tests, desktop tests, Release build, diff check, and strict
  OpenSpec validation.
