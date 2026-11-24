# Change 06 Projects and Batch Runs Report

## Conclusion

Change 06 adds a local, non-identifying Project hierarchy and a SQLite-backed
single-worker Run queue. It changes only the new `/api/runs` execution behavior
to return `202 Accepted`; existing Viewer, configured spectrum, configured
spectrogram, and legacy analysis routes remain available and use the same
backend scientific calculations.

## Evidence

- Project tests verify internal subject-code uniqueness, rejected PII fields,
  session association, project-boundary rejection, and repeatable schema
  migration.
- Queue tests verify HTTP 202 enqueue/poll behavior, idempotency, single claim,
  cancellation, retry parent provenance, restart recovery, and persistence of
  100 queued jobs without duplicate IDs.
- Batch tests verify project membership, frozen request identity, deterministic
  duplicate handling, cancellation, completed analysis, missing-channel, and
  insufficient-duration outcomes.

## Limits

The worker is intentionally single-threaded. Running numerical functions use
cooperative cancellation and cannot be forcefully interrupted mid-call. A
restart recovers persisted jobs by requeueing them under the original Run ID.
No conclusion about clinical validity is implied by queue completion or batch
status.
