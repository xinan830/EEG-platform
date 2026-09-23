## Context

See `proposal.md` for motivation. The current review session stores a Python
checkpoint at the end of each completed 10-second filtered source chunk. A miss
for chunk N recursively builds chunks 0 through N-1, which is scientifically
correct but also writes and returns display values for history that a distant
foreground jump does not need.

The Python service already exports and imports versioned causal filter state.
Raw recordings are immutable sample-major float64/V data, and gaps divide them
into independent contiguous sample-counter segments. Review filtering must
remain Python-owned and independent from acquisition lifecycle state.

## Goals / Non-Goals

**Goals:**

- Reuse exact causal state across review sessions and distant seeks.
- Advance missing history in bounded batches without materializing every
  intermediate display chunk.
- Make cold-seek progress, cancellation, failure, and atomic frame publication
  deterministic.
- Bound memory, disk, request concurrency, and queued work.
- Keep components below the project's size thresholds or register a justified
  exception before exceeding them.

**Non-Goals:**

- Making the first cold distant seek independent of the number of preceding
  contiguous samples; exact causal filtering still has to consume them once.
- Approximating a 0.01 Hz high-pass state with a short warm-up.
- Changing raw recording files, public recording APIs, montage mathematics,
  acquisition state, or the Python filter algorithm.
- Treating engineering equivalence tests as clinical validation.

## Decisions

### 1. Store checkpoint anchors as a separate derived-cache artifact

Add a checkpoint-anchor cache alongside, but independent from, filtered source
chunks. Each completed anchor records a versioned group fingerprint, contiguous
segment origin, next sample counter, and opaque Python checkpoint payload. An
atomic temporary-write/rename makes the anchor discoverable only after all
metadata and bytes are durable.

The group fingerprint covers recording/session and raw-manifest identity,
sampling rate, ordered source-channel schema and V unit, filter settings,
Python algorithm/contract fingerprint, checkpoint format version, and processing
semantics. Lookup chooses the greatest compatible next sample counter not after
the requested processing start and never crosses a segment origin.

Alternative: infer anchors by recursively opening cached display chunks. This
preserves today's dependency chain and forces discovery and construction to
remain coupled to chunk files. A small explicit index makes nearest-anchor
lookup and bounded eviction testable without loading waveform payloads.

### 2. Advance causal history without retaining intermediate display values

Introduce a review checkpoint coordinator that creates one Python filter
session for a work plan, imports the selected anchor, and streams bounded raw
batches in sample-counter order. For history before the requested display
range, it uses the existing binary warm-up/advance path and discards returned
waveform values. At configured anchor boundaries it exports state and atomically
publishes a new anchor. It requests filtered values only for source chunks that
the target frame or nearby bounded prefetch actually needs.

Every batch validates channel count/order, unit, and counter continuity before
it advances state. On a gap, the coordinator closes the old chain, creates a
fresh Python session, and treats the first post-gap sample as a new segment
origin. Checkpoints remain opaque to C#; compatibility is validated on both the
desktop key and Python import boundary.

Alternative: add a backend job that owns recording files and prefilters the
whole recording. That duplicates desktop raw-recording ownership, adds a new
long-running API lifecycle, and broadens migration unnecessarily. The existing
bounded filter-session API is sufficient.

### 3. Use one prioritized coordinator per recording/filter provenance

Foreground requests publish a latest target to the coordinator. One worker per
provenance group resumes from the nearest completed anchor and advances toward
that target. A newer target replaces queued intent; it does not delete already
completed anchors. If it lies behind the worker's current state, the coordinator
selects the nearest compatible earlier anchor and starts a new work plan rather
than reversing a causal session.

Speculative work runs only when there is no foreground target, uses the same
bounded worker, and yields on navigation, playback demand, filter changes, or
session disposal. The initial policy extends forward from the current completed
chain; it does not launch whole-record parallel scans.

Alternative: build multiple distant ranges concurrently. Without a preceding
anchor, each range repeats history and increases Python, disk, and memory load;
it cannot improve exact first-pass complexity.

### 4. Report sample-based preparation state separately from playback state

Review exposes a preparation snapshot containing phase, contiguous-segment
start, processed next sample counter, required target counter, and an optional
structured failure. Percentage is derived only when both counters belong to
the same known segment. UI status may format this information, but it must not
estimate scientific position from elapsed wall time or move the navigator to an
unloaded target.

The session revision remains the atomic frame guard: completed work for an old
target may populate derived caches but cannot commit viewport, cursor, or chart
data after a newer request.

Alternative: show a generic spinner. It hides whether work is progressing and
makes long exact preparation indistinguishable from a hang.

### 5. Apply explicit cache and lifecycle bounds

Anchor lookup metadata is loaded lazily per recording/filter group. Storage has
centrally defined byte/count bounds and least-recently-used eviction; eviction
removes derived anchors only. In-memory plans retain only a bounded raw batch,
one opaque checkpoint, target output chunks, and small index metadata.

Disposal and filter changes cancel active and speculative plans and close their
Python sessions. Failures keep the previous complete frame and expose the
failed preparation phase; no raw fallback is labelled as filtered.

Alternative: retain every 10-second anchor forever. Long recordings multiplied
by filter combinations would create unbounded disk and index growth.

## Risks / Trade-offs

- [First cold seek still processes all earlier contiguous samples] → State this
  explicitly in UI/reporting and rely on persistent anchors plus idle extension
  for subsequent seeks.
- [Anchor key omits scientific provenance] → Centralize key construction and
  test every recording, channel, unit, filter, contract, version, segment, and
  counter field independently.
- [Cancellation races publish obsolete frames] → Separate reusable cache
  publication from revision-guarded frame commit and test rapid target changes.
- [Gap discovery invalidates an imported chain] → Validate every raw batch's
  first/last counters and reset before processing post-gap values.
- [Background work competes with playback] → Use one bounded worker and strict
  foreground preemption rather than parallel speculative jobs.
- [Disk corruption or abrupt process exit] → Use completed-only atomic files,
  validate all headers/payloads, and treat failures as cache misses.
- [Review session grows beyond 400 lines] → Extract coordinator, anchor cache,
  progress model, and work-plan logic by responsibility; update
  `docs/code-size-policy.json` for any remaining file above the soft limit.

## Migration Plan

1. Add the versioned anchor model/cache and compatibility tests without using
   it for frame construction.
2. Add the coordinator and equivalence tests comparing sequential full-history
   filtering with anchor resume across several sampling rates and filter sets,
   including 0.01 Hz high-pass and recorded gaps.
3. Route review source-chunk construction through the coordinator while keeping
   existing filtered chunk files as disposable target-output cache entries.
4. Add progress/failure binding and rapid-seek/playback/cancellation tests.
5. Measure cold and warm distant seeks with bounded synthetic long recordings,
   run desktop/backend/OpenSpec gates, and record the engineering validation.

Rollback disables the coordinator and ignores the new anchor directory;
immutable recordings and existing cache formats remain readable. New anchors
are disposable and may be deleted without migration.
