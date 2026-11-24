## 1. Runtime identity

- [x] 1.1 Add exact scientific-version pinning to official Run resolution and execution.
- [x] 1.2 Resolve and persist immutable official Definition IDs and versions for sync and queued Runs.

## 2. Module boundaries

- [x] 2.1 Move official lifecycle metadata to runnable module manifests and derive the official catalog from them.
- [x] 2.2 Add a module-owned execution snapshot and remove concrete FAA branches from generic Run resolution.
- [x] 2.3 Validate common and extension evidence envelopes at the runtime boundary.

## 3. Verification

- [x] 3.1 Add regression tests for queue identity, version pinning, catalog identity and module snapshots.
- [x] 3.2 Run backend tests, frontend tests/build, OpenSpec strict validation and diff checks.
