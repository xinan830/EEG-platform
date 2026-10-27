## 1. ANT location contract

- [x] 1.1 Add regression coverage for resolving ANT system profile labels, preserving REF/GND separation, and retaining unavailable-location behavior.
- [x] 1.2 Attach resolver results to the generated ANT channel configuration without changing order, labels, display selection, or channel kinds.
- [x] 1.3 Surface resolver failures as explicit status while keeping the channel configuration usable.

## 2. Montage direction integration

- [x] 2.1 Verify newly created montage profiles snapshot ANT channel locations and formula direction remains a projection, not separately stored state.
- [x] 2.2 Run focused WPF tests, OpenSpec strict validation, and `git diff --check`.
