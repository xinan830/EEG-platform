## 1. Native Adapter

- [x] 1.1 Add C ABI structs, delegates, dynamic loader, version/error mapping, and x64 guards.
- [x] 1.2 Add explicit ANT/eego options, device identity parsing, actual discovery, native range/channel mapping, and an opt-in adapter.
- [x] 1.3 Add one-stream lifecycle and sample-major stream reader with sample-counter validation.
- [x] 1.4 Preserve all-device channel order and per-channel units; do not create electrode labels.

## 2. Verification And Documentation

- [x] 2.1 Add hardware-free unit tests for mapping, counter validation, unavailable SDK, and no-default-range behavior.
- [x] 2.2 Add SDK installation and manual hardware acceptance documentation without committing vendor material.
- [x] 2.3 Run Release build, tests, OpenSpec strict validation, and `git diff --check`.
