## Context

Dynamic results have durable SQLite identities and immutable JSON/NPZ files. Review has separate local filtered-source and display caches. Clearing files alone breaks result reads. Review runs in a separate window and may prefetch while paused; navigation to Settings is not proof of inactivity.

## Goals / Non-Goals

Provide a supported one-button reset with accurate failures. Preserve all science and historical data. Do not add configurable paths, scheduled cleanup, scientific recomputation, automatic corrupt-result recovery, database compaction, or cross-installation discovery.

## Decisions

- A small HTTP maintenance gate counts complete in-flight storage/analysis API requests, acquires an exclusive boundary for the clear endpoint, and rejects contention rather than waiting indefinitely. Health and live-filter/live-recording batch APIs are independent: their transient buffers never access the persisted cache. This protects result reads and full-Run admission, not only dynamic ensure.
- A dedicated maintenance use-case owns transactional child-first row deletion and fixed-directory file deletion. It holds the dynamic recovery/admission owner lock and rejects pending executor jobs and queued/running Runs or attempts before changing data. Recovery uses the same lock. No background task is forcibly cancelled.
- Records are committed absent before files are deleted. Failure after that point leaves unreferenced files and an explicit partial error, never live records referencing removed files. Directory accounting is rescanned even on failure; all dynamic artifact-reader caches and delivery buffers are invalidated.
- WPF owns local review-cache deletion and never writes backend SQLite. RecordingSessionManager excludes maintenance from open/closing recording sessions and prevents new leases during cleanup. HTTP and local file work stay off Dispatcher; the ViewModel owns progress/errors and the View only binds.
- Only the connected backend is affected. No backup of rebuildable caches per click: copying an entire database would add unnecessary disk/time cost. Historical data and schema are not deleted. Index definitions remain intact; “index cleanup” means cached dynamic records.

## Alternatives

Filesystem-only cleanup was rejected because it creates dangling READY records. Killing/restarting services was rejected because maintenance must cooperate with current owners. Allowing cleanup during analysis was rejected because a worker can publish after a directory scan. A separate configurable multi-database tool is out of scope.

## Risks / Trade-offs

Cold calculation after cleanup can be slower; this action does not repair scheduling lag. Active or long-lived requests can require retrying later. Files locked by external processes cause explicit partial failure. Cross-process sharing of one storage root remains unsupported, consistent with existing artifact-store ownership. No EEG API/WebSocket shapes or scientific semantics change.

## Migration / Rollback

No migration. Roll back the source/UI/API changes if needed; deleted cache content regenerates. Do not restore old dynamic references without their matching artifact files.

## Validation

Test real fixture windows, missing files, protected rows/files, repeated clear and successful recomputation; test pending work, HTTP/read/recovery contention, deletion failure and accounting. Test desktop command state, HTTP contract, local allowlist/link checks and recording-access exclusion. Build BAML, run relevant tests, strict OpenSpec validation and diff/size checks. Record GUI acceptance separately from automated results. Existing unrelated active changes are retained; the user explicitly requested this separate feature.
