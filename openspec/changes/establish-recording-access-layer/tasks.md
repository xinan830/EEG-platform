## 1. Contract and errors

- [x] 1.1 Add recording access metadata, sample-range, segment and read-result
  types under the recording-owned desktop module.
- [x] 1.2 Add stable access error codes and validation for identity, sampling
  rate, channels, units, sample bounds, gaps and half-open ranges.
- [x] 1.3 Add focused contract tests for boundaries, channel order, V units,
  explicit gaps, no-data ranges, cancellation and structured failures.

## 2. Historical local adapter

- [x] 2.1 Adapt the existing local raw recording reader to the recording access
  contract while preserving immutable raw chunks and persisted lifecycle data.
- [x] 2.2 Move validated index/reader lifetime into a recording session manager
  keyed by recording identity and source revision.
- [x] 2.3 Add tests proving duplicate opens share the session lifetime and close
  waits for admitted reads without leaking a slot.

## 3. Review migration

- [x] 3.1 Change Review source loading, frame construction and composition to use
  recording access sample ranges and returned metadata.
- [x] 3.2 Remove the old production `IRecordingReviewReader` injection path and
  update Review fakes/fixtures to the new contract; do not add a compatibility
  adapter.
- [x] 3.3 Verify Review still preserves gaps, source units, absolute sample
  coordinates, cancellation and latest-revision publication behavior.

## 4. Verification and documentation

- [x] 4.1 Build the isolated WPF project and run affected desktop contract and
  Review tests; record any environment-limited tests explicitly.
- [x] 4.2 Run `openspec validate --change establish-recording-access-layer
  --strict` and `git diff --check`.
- [x] 4.3 Update the desktop architecture documentation with the new access
  boundary and explicitly defer the live ring-buffer adapter to a later change.
