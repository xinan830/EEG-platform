# Remove Retired User-Definition Executor

## 1. Inventory and freeze

- [x] Record the current active callers of `UserDefinitionAlgorithm`, the
  legacy Definition engine, and the playback processor.
- [x] Preserve the retired catalog/API contract and historical Run readers.
- [x] Freeze new HTTP Run callers to the retired boundary; new
  `definition_metric` Run creation returns the stable `410` response.

## 2. Migrate active execution callers

- [x] Convert production queued `definition_metric` execution to an explicitly
  unavailable response while preserving historical result reads.
- [x] Remove the production queue's active path to `UserDefinitionAlgorithm`
  and remove the compatibility execution flag.
- [x] Keep current Definition validation and scalar preview callers behind the
  stable `app.legacy.definition_engine` compatibility boundary; they do not
  import or execute the retired EEG metric executor.
- [x] Keep unrelated legacy biofeedback playback behind its existing processor
  boundary; its migration is a separate streaming-science change, not a
  precondition for removing the user-definition executor.

## 3. Prove historical compatibility

- [x] Read historical Definition and Run records without importing executable
  user-definition code.
- [x] Verify queue recovery, artifact loading, and result export for historical
  user-definition Runs.
- [x] Add architecture tests proving no production or opt-in path loads the
  removed user-definition EEG executor.

## 4. Remove only after gates pass

- [x] Confirm `rg` has no remaining executable metric-runner or
  `UserDefinitionAlgorithm` import in application code.
- [x] Delete the retired metric runner, Runtime adapter, and their obsolete
  facades. Replace execution-only tests with read-only historical regression
  coverage.
- [x] Run the full backend suite, OpenSpec strict validation, and diff checks.
- [x] Record rollback point `b822e17` (before physical executor deletion) and
  archive this change after the deletion tests pass.
