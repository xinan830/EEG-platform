## 1. Desktop Shell

- [x] 1.1 Create the standalone x64 .NET 10 WPF solution and project structure.
- [x] 1.2 Add typed backend-health and device-adapter boundaries with bounded failure behavior.
- [x] 1.3 Build the desktop workspace with explicit backend, device, acquisition, and scientific ownership states.
- [x] 1.4 Add the non-SDK acquisition core: lifecycle coordination, channel/batch contracts, bounded display buffering, sample-counter continuity, raw chunk writing, and bounded analysis handoff.

## 2. Verification And Documentation

- [x] 2.1 Add focused unit tests for health mapping, unavailable-device state, acquisition lifecycle, continuity, bounded buffering, and raw chunks.
- [x] 2.2 Document local desktop build, acquisition-core ownership, and the intentionally deferred hardware/back-end work.
- [x] 2.3 Run build, tests, OpenSpec strict validation, and `git diff --check`.
