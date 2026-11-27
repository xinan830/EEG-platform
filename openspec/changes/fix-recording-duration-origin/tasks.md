## 1. Reproduce
- [x] 1.1 Add deterministic regressions for preview-to-record, pause before first recorded batch, resume and nonzero device counters; confirm failure before production edits.

## 2. Fix
- [x] 2.1 Require an explicit recording origin for recording-duration and pause accounting; update all internal callers and fixtures while retaining lifecycle priority and display continuity.

## 3. Verify
- [x] 3.1 Verify repeated lifecycle pauses and both monitor entry points; run focused, domain and full desktop tests, backend gate, size/diff and strict OpenSpec checks; record exact outcomes.
