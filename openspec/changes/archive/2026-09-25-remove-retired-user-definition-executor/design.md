# Retired user-definition executor removal

## Boundary

`definition_metric` is retained as a persisted historical Run type, not an
executable algorithm. The public API, synchronous Run service, and persistent
queue reject new work. A queued historical job recovered after restart fails
with `USER_DEFINED_ALGORITHM_RETIRED` before resolving EEG or constructing a
calculator. Completed historical Runs and NPZ artifacts are read from storage.

Definition validation and scalar simulation preview remain separate from EEG
metric execution. They may use the closed legacy graph vocabulary but never
read raw samples or instantiate the retired metric runner.

## Removal

Delete the metric runner, Runtime user-definition module, opt-in execution
flag, and definition-specific result serializer. Keep the generic Run model's
`definition_metric` discriminator so stored rows still deserialize. Do not
rewrite existing rows, cache keys, evidence, or artifacts.

## Verification And Rollback

The rollback point before physical deletion is commit `b822e17`. Verify
static and dynamic historical result summaries, artifact listing, queued-job
recovery, and direct/API rejection. The full backend test suite and strict
OpenSpec validation must pass. Reverting this deletion commit restores the
old compatibility code without a database migration.

The legacy biofeedback `/api/playback` processor is unrelated to the
user-definition EEG executor. Its stateful filter and metric behavior require
a separate migration and are not changed here.
