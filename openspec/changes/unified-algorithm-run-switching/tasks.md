## 1. Shared execution lifetime

- [x] 1.1 Add generation-aware foreground Run ownership, cancellable polling, and late-created Run cancellation to the shared coordinator.
- [x] 1.2 Route all catalog configuration invalidations through that owner and allow the latest run to start without stale polling blocking it.
- [x] 1.3 Preserve separate combined background Run and preview target lifetimes.

## 2. Verification

- [x] 2.1 Add regression coverage for full-to-on-demand switching while Run creation or polling is pending, plus algorithm/configuration replacement.
- [ ] 2.2 Run desktop tests/build, OpenSpec strict validation, file-size review, and `git diff --check`.
