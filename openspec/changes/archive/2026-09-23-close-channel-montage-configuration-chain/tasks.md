## 1. Channel Configuration

- [x] 1.1 Add pure channel-profile validation.
- [x] 1.2 Make profile application publish state only after persistence succeeds.
- [x] 1.3 Distinguish exact active snapshots from unapplied catalogue edits.
- [x] 1.4 Lock signal fields and deletion while montage references exist.
- [x] 1.5 Add an explicit create-new-version workflow for locked profiles.

## 2. Montage Configuration

- [x] 2.1 Compare saved montage source snapshots with current channel profiles.
- [x] 2.2 Display montage source synchronization status in the catalogue.

## 3. Verification

- [x] 3.1 Add tests for invalid profiles, failed application, unapplied edits, and stale montage snapshots.
- [x] 3.2 Run desktop tests, strict OpenSpec validation, and diff checks.
- [x] 3.3 Test reference locking, metadata-only edits, deletion protection, and version creation.
