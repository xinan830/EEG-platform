## Context

The real-time filter is causal and owns state in local Python. Existing display
rendering rebuilds full SciChart series from an in-memory snapshot, so source
history must be complete when that snapshot changes.

## Goals / Non-Goals

**Goals:**

- Eliminate empty partial windows and mixed configuration results.
- Refilter enough contiguous raw context to initialize a replacement filter,
  then retain its final state for later batches.
- Keep expensive candidate computation off the capture and WPF UI threads.

**Non-Goals:**

- Alter raw recordings, cross sample-counter gaps, or describe display
  filtering as a scientific preprocessing Run.
- Guarantee that very-low-cutoff display filters are fully settled with a
  bounded real-time history; the pre-roll horizon is explicitly bounded.

## Decisions

- Retain the old filtered buffer until a Python-produced candidate contains the
  full visible display window. Replacing it early was rejected because it
  produces observable blank time regions.
- Send a single contiguous source span consisting of high-pass-dependent
  pre-roll plus current visible samples. The returned tail is the candidate
  window; Python keeps state after the whole span for future data.
- Version configuration results. A late result from an earlier revision is
  discarded instead of appended to the new screen.

## Risks / Trade-offs

- [Low high-pass values require long histories] → Bound the pre-roll at 120 s
  and present it as display stabilization, not a scientific quality guarantee.
- [Large local HTTP request takes time] → Existing old display remains visible;
  raw capture is outside this bounded analysis path.
- [Repeated changes race] → Only the newest configuration revision may swap the
  display buffer.

## Migration Plan

1. Keep API warm-up fields compatible with zero/empty values.
2. Deploy candidate generation and revision-gated atomic swap together.
3. Rollback returns to fixed-before-start filter selection; raw files need no
   migration because they are not filtered in this path.
