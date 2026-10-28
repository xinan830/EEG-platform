## Context

See proposal.md and the two delta specifications. The current desktop/backend path already has dynamic window and stream components, but their ownership of playback targets, cancellation, preparation and publication overlaps. The implementation must preserve absolute recording-relative time, half-open sample ranges, explicit units, channel order, recording/montage identity and existing full-run behavior.

## Goals / Non-Goals

**Goals:**

- Make one playback context the source of truth for cursor and generation.
- Introduce one bounded admission point that classifies work without forcing all algorithms into one execution model.
- Establish RBP as the first end-to-end adapter with exact-window deduplication and ordered history projection.
- Make queue wait, compute time, result lag, cancellation and publication observable.
- Keep raw persistence independent and preserve existing full Analysis Run compatibility.

**Non-Goals:**

- Do not introduce Dask, Celery, Redis, LSL, BrainFlow or a distributed worker system.
- Do not redesign every TNK algorithm in this change.
- Do not make stateful TNK algorithms latest-only when doing so would violate checkpoint order.
- Do not change scientific filtering, reference, sample rate, units or montage semantics to improve throughput.
- Do not treat successful engineering playback as clinical validation.

## Decisions

### One context and one target stream

The desktop playback context owns recording identity, montage/configuration identity, sample rate, cursor sample, playback generation and seek/restart transitions. It emits target envelopes; waveform and analysis subscribers do not create independent clocks. A target envelope includes a stable trace id, generation, requested cursor, backend-planned window identity once resolved, and cancellation token/subscription identity.

Alternative considered: letting each chart poll the cursor and create its own request. Rejected because it creates duplicate targets and makes result ordering impossible to reason about.

### Explicit task classes with separate policies

The scheduler has four logical classes: waveform preview (latest target), window-independent analysis (bounded latest-target/coalescing), stateful analysis (ordered checkpoint chain), and batch work (low-priority bounded admission). They share admission, identity, resource accounting and diagnostics, but not execution semantics.

Alternative considered: one FIFO queue for all work. Rejected because expensive or stale analysis can block waveform responsiveness and because stateful work cannot be arbitrarily reordered.

### Shared preparation owned by the backend data layer

A preparation key contains source recording hash/identity, montage/reference identity, channel order, sample rate, preprocessing contract/version and the half-open sample range or continuous-stream checkpoint. The data layer owns the prepared-window cache and single-flight map. RBP consumes this prepared object and does not reread the raw source. Waveform display may derive its own display-only downsampled representation from the same source window without changing scientific preparation.

Alternative considered: one universal final preprocessing array for every algorithm. Rejected because algorithms can require different filters, references, warm-up and normalization. The shared layer therefore exposes canonical data plus explicit algorithm-specific preparation contracts.

### RBP adapter and result projection

The first adapter maps a scheduler target to the existing RBP scientific contract. The backend returns a result containing recording/montage identity, channel order, actual half-open sample range, five named bands, ratio units, quality/evidence, algorithm version and preparation provenance. The WPF projection maintains bounded per-channel histories keyed by generation and scientific window end time. Missing or rejected values remain gaps with reasons.

Alternative considered: append results in completion order. Rejected because compute completion is not the scientific time axis and would produce the gaps and jumps visible in the current chart.

### Cancellation and resource ownership

Cancellation is cooperative and generation-scoped. Superseding a window-independent target cancels admission/subscription and asks the backend computation to stop at safe checkpoints; the worker must release its resource lease before the replacement target is admitted. A subscriber cancellation does not cancel shared work still required by another subscriber. Stateful TNK cancellation is allowed only at a checkpoint that can be resumed or replayed safely.

Alternative considered: immediately kill every old task on each cursor tick. Rejected because native model execution and stateful computation may not be safely interruptible and can cause starvation.

### Diagnostics before optimization

Every target emits structured diagnostics for queue admission, preparation, compute, publication and cancellation. Required metrics include queue depth, queue wait, compute duration, result lag relative to cursor, replayed sample count, preparation cache hit/miss and raw-read count. Diagnostics are opt-in in the ordinary UI and are stored with a trace id for focused tests and real-recording replay.

## Risks / Trade-offs

- [Risk] RBP computation remains slower than the playback cadence. -> Use bounded latest-target admission, expose result lag, and verify no unbounded queue growth before enabling other adapters.
- [Risk] Shared preparation cache reuses data under an incomplete identity. -> Include recording hash, montage/channel order, preprocessing version, window policy and sample bounds in the key; reject conflicts.
- [Risk] Cooperative cancellation leaves a native worker running longer than expected. -> Measure cancellation release latency and keep a finite analysis resource lease; do not claim cancellation at request time alone.
- [Risk] Out-of-order results expose projection bugs. -> Use deterministic out-of-order tests and generation fences before GUI acceptance.
- [Risk] RBP-specific assumptions leak into later stateful algorithms. -> Keep the scheduler contract algorithm-neutral and require separate adapters for checkpoint state, warm-up and replay.
- [Risk] Existing unfinished RBP history work conflicts with this change. -> Reuse compatible result contracts, isolate the scheduler integration behind a flag, and preserve the existing full-run path for rollback.

## Migration Plan

1. Freeze new TNK real-time adapters and record the current behavior as a baseline.
2. Add scheduler contracts, context generation and diagnostics without changing full-run APIs.
3. Connect waveform preview to the unified playback target while keeping raw persistence independent.
4. Connect only RBP, first in an off-by-default mode; verify shared preparation counts and ordered history projection.
5. Run backend tests, scheduler tests, WPF projection tests, OpenSpec validation, git diff --check, and isolated desktop build.
6. Perform a real-recording playback comparison with waveform-only and waveform+RBP, measuring tick interval, queue depth, result lag, raw reads, cache hits and chart coverage.
7. Enable RBP only after acceptance. Migrate other window-independent algorithms one at a time; design separate checkpoint adapters for stateful TNK.

Rollback disables the unified RBP flag and stops admitting new real-time analysis targets. It does not delete published results, raw recordings or existing full-run artifacts. A failed generation is fenced from later publication.

## Open Questions

- The exact playback cadence and RBP window step must be measured against the target recording sample rate before setting default bounds.
- The initial analysis CPU budget and maximum prepared-window memory budget should be selected from real-recording diagnostics, not guessed from the screenshots.

