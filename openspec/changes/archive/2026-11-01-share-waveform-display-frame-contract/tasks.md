## 1. Shared display contract

- [x] 1.1 Add a typed request context, payload envelope, and exact-identity publication gate.
- [x] 1.2 Add focused tests for current, stale, canceled, and invalid request identities.

## 2. Consumer integration

- [x] 2.1 Capture the local recording identity and source revision for TNK preview requests; publish only through the shared gate.
- [x] 2.2 Route Review asynchronous frame commits through the shared gate while preserving its cache-hit path.

## 3. Verification

- [x] 3.1 Run focused desktop tests and build the WPF project.
- [x] 3.2 Validate the OpenSpec change and inspect the final diff for unintended sample, unit, channel, filter, or API changes.
