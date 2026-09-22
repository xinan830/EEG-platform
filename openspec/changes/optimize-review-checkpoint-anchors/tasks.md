## 1. Compatibility Contract and Test Fixtures

- [x] 1.1 Add focused tests proving the anchor compatibility key changes for recording/manifest identity, sampling rate, ordered source-channel schema, V unit, every filter setting, Python contract/checkpoint version, segment origin, and next sample counter.
- [x] 1.2 Add deterministic long-record fixtures that stream bounded sample-major float64/V batches with configurable sampling rate, channel order, target distance, and explicit sample-counter gaps without allocating a complete recording in memory.
- [x] 1.3 Add Python checkpoint equivalence coverage for sequential filtering versus export/import resume, including the supported 0.01 Hz high-pass configuration, mismatched provenance rejection, and gap-triggered fresh state.

## 2. Versioned Anchor Storage

- [x] 2.1 Implement a focused checkpoint-anchor key and metadata model with centralized versioned fingerprint construction and no waveform payload ownership.
- [x] 2.2 Implement completed-only atomic anchor writes and nearest-compatible-earlier lookup outside the immutable recording directory.
- [x] 2.3 Implement explicit byte/count bounds, lazy index loading, least-recently-used eviction, and cancellation-safe cleanup for derived anchors.
- [x] 2.4 Add storage tests for round-trip lookup, nearest selection, cross-segment rejection, corrupt/truncated files, interrupted writes, incompatibility, eviction, and raw-recording immutability.

## 3. Exact Checkpoint Coordination

- [x] 3.1 Extend the desktop review-filter boundary with bounded session operations that import an opaque checkpoint, advance history without retaining display values, export anchors, and return target values while preserving Python ownership.
- [x] 3.2 Implement a checkpoint work planner that selects the nearest compatible anchor or contiguous-segment origin and emits bounded, strictly ordered raw ranges through the requested target.
- [x] 3.3 Implement one latest-target foreground coordinator per recording/filter provenance with bounded concurrency, reusable completed anchors, cancellation, and clean Python-session disposal.
- [x] 3.4 Detect every raw counter discontinuity before advancing state, terminate the prior chain, and restart at the first post-gap sample without fabricating data.
- [x] 3.5 Add coordinator equivalence tests across cold start, warm anchor resume, reverse seek, rapid superseding seeks, gaps, cancellation, Python failure, and disposal.

## 4. Review Session and User-Visible State

- [x] 4.1 Route HTTP-filtered source-chunk construction through the coordinator so intermediate history advances state without creating unnecessary display chunks, while target chunks retain their existing atomic cache behavior.
- [x] 4.2 Add a preparation-state model with phase, contiguous-segment origin, processed next sample counter, required target counter, and structured failure; derive progress only from compatible sample-counter ranges.
- [x] 4.3 Bind review status to exact-history preparation, completion, cancellation, and failure without advancing the navigator or labelling raw/unready data as filtered.
- [x] 4.4 Preserve the last complete frame and revision-guarded atomic commit during cold distant jumps, playback crossings, filter changes, and obsolete target completion.
- [x] 4.5 Add bounded idle forward anchor extension that yields to foreground navigation/playback and stops on filter change or review-session disposal.
- [x] 4.6 Add session/view-model tests for progress, foreground preemption, obsolete result suppression, prior-frame retention, atomic target commit, and background-work shutdown.

## 5. Maintainability and Validation

- [x] 5.1 Extract anchor cache, work-plan, coordinator, and progress responsibilities from `RecordingReviewSession`; measure every changed business file and update `docs/code-size-policy.json` for any file above 400 lines.
- [x] 5.2 Add bounded performance tests comparing cold distant seek, repeated seek using persisted anchors, and nearby seek, recording processed sample counts, peak bounded buffers, Python calls, and cache writes without claiming clinical validation.
- [x] 5.3 Update the desktop acquisition/review architecture and create a validation report documenting scientific invariants, first-cold-seek complexity, cache limits, failure behavior, and rollback.
- [x] 5.4 Run the full desktop Release test suite and build, backend checkpoint and full test suites, strict OpenSpec validation, and `git diff --check`; record exact results and any environment limitations.
