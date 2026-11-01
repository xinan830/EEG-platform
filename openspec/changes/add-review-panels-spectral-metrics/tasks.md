## 1. Review extension boundary

- [x] 1.1 Add a registration contract for algorithm-owned Review configuration, result presentation, and panel selection; keep cursor, cancellation, identity fencing, and subscription lifecycle algorithm-neutral.
- [x] 1.2 Migrate existing RBP and STFT Review panels to the registration boundary without changing their request or chart behavior.

## 2. Algorithm-owned scalar panels

- [x] 2.1 Add FAA and Brainbeat Review registrations with explicit ordered F3/F4 and Fz/Pz source selection, montage mappings, current catalog policy, and invalid-role failures.
- [x] 2.2 Add IAPF and Theta/Beta Review registrations with one selected channel, current catalog policy, and exact output/unit validation.
- [x] 2.3 Add the shared scalar history/chart presentation for validated backend values, preserving returned time/range, units, and failure state; start scalar and RBP visible ranges at the cursor when the panel opens.
- [x] 2.4 Add the four Review menu/toolbar entries and bind them to the existing one-active-panel host.

## 3. Verification

- [x] 3.1 Add focused adapter, configuration, role/montage, output/unit, quality/failure, stale-response, switch/close, and paused-cursor tests for all four algorithms.
- [x] 3.2 Run focused backend capability checks only if backend code changes, focused desktop tests, isolated WPF build, OpenSpec strict validation, diff checks, and touched-file size checks.
- [ ] 3.3 Record manual acceptance for all four panels on a local recording, including a montage with missing roles and verification that playback remains usable.

Evidence: `docs/validation/2026-11-01-review-scalar-algorithm-panels.md`.
