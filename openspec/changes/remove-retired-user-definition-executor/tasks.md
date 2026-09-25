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
- [x] Remove the production queue's active path to `UserDefinitionAlgorithm`;
  the implementation is available only behind an explicit compatibility flag
  used by migration tests.
- [x] Keep current Definition validation and scalar preview callers behind the
  stable `app.legacy.definition_engine` compatibility boundary; they do not
  import or execute the retired EEG metric executor.
- [x] Isolate playback construction behind one service-level processor
  boundary without changing existing playback payloads or metric behavior.
- [ ] Migrate playback orchestration away from the legacy `EEGProcessor` when
  an equivalent Runtime-backed path is available.

## 3. Prove historical compatibility

- [x] Read historical Definition and Run records without importing executable
  user-definition code.
- [x] Verify queue recovery, artifact loading, and result export for historical
  user-definition Runs.
- [x] Add architecture tests proving the default production path does not load
  or execute user-definition code; retain the explicit compatibility path for
  migration tests.

## 4. Remove only after gates pass

- [x] Confirm `rg` has no eager active imports outside historical readers, tests,
  and explicit compatibility boundaries.
- [x] Delete the obsolete `services/definition_metric_runner.py` facade. The
  legacy executor remains only behind the explicit compatibility switch until
  its remaining migration tests are replaced by read-only historical fixtures.
- [x] Run the full backend suite, OpenSpec strict validation, and diff checks.
- [ ] Record the rollback point and archive this change.
