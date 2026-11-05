## 1. Backend scientific contract

- [x] 1.1 Add validated TBR frequency-mode configuration and standard/individualized compute paths with exact band evidence.
- [x] 1.2 Add numerical tests for both modes, IAPF independence in standard mode, unclamped low-IAPF bands, and unavailable out-of-axis bands.
- [x] 1.3 Declare the TBR 20-second dynamic policy, suppress warm-up values, and align Official Run defaults and validation with the module policy.
- [x] 1.4 Bump TBR scientific, implementation, and persisted Definition identities and update focused provenance/catalog tests.

## 2. Desktop parameter flow

- [x] 2.1 Add the Official Run mode selector and serialize the selected value in static and dynamic configurations.
- [x] 2.2 Add the Review mode selector, serialize it for on-demand windows, and trigger recomputation when it changes.
- [x] 2.3 Add WPF tests for mode defaults, serialized requests, Review recomputation, and backend evidence presentation.

## 3. Validation and documentation

- [x] 3.1 Run focused TBR backend and Official Run tests plus affected WPF tests and build.
- [x] 3.2 Run strict OpenSpec validation and `git diff --check`; record validation limits and confirm no unrelated worktree changes were overwritten.
