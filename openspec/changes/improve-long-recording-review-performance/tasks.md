# Tasks

## Phase 0 - Evidence Before Behavior Changes

- [ ] Record the current revision, test environment, and review performance
  matrix for 10 s, 10 min, 1 h, 24 h, and a multi-day fixture.
- [x] Add opt-in diagnostics for navigation targets, raw reads,
  filter/checkpoint source, frame publication, and SciChart binding. Each
  sample carries recording and review-session identities, revision, absolute
  time range, source, point count, and elapsed time without raw signal values.
- [ ] Extend the evidence path to algorithm result lookup/computation, request
  counts, and bounded memory observations.
- [ ] Distinguish `window-preview` read hits from actual filter or algorithm
  execution in logs and tests.
- [ ] Measure continuous playback for unbounded DataSeries growth, dispatcher
  cost, stale response publication, and repeated warmup calls.
- [ ] Write the Phase 0 validation report with evidence and a root-cause
  decision before introducing a display-index implementation.

## Phase 1 - Bounded Current Review Path

- [ ] Add regression tests for requested/loaded/published identity and obsolete
  response fencing.
- [ ] Keep visible chart points, frame cache, in-flight work, and prefetch within
  explicit budgets; preserve gaps and units.
- [ ] Keep waveform rendering independent from algorithm-detail refreshes and
  avoid rebuilding unchanged trace data.
- [ ] Validate forward/reverse drag, rapid target changes, playback, cold seek,
  cached seek, filter changes, and recording gaps.

## Phase 2 - Multi-Resolution Waveform Display

- [ ] Define the level-selection and bucket/extrema contract from viewport
  pixels, time range, sampling rate, channel count, and source identity.
- [ ] Implement chunked, indexed, disposable display-level storage with atomic
  publication and explicit gap/invalid-sample handling.
- [ ] Build filtered levels only from compatible filtered source chunks; keep
  raw and filtered display identities separate.
- [ ] Add overview/detail zoom tests and bounded-memory/read tests for long
  recordings.

## Phase 3 - Full-Result Reuse And Completion

- [ ] Route complete full-analysis windows through indexed read before the
  on-demand producer, preserving the existing scientific identity and
  provenance.
- [ ] Add regression tests proving no duplicate filter warmup or algorithm
  computation for a compatible completed window.
- [ ] Add fallback tests for missing, stale, incompatible, corrupt, and partial
  artifacts.
- [ ] Run backend tests, focused/full WPF tests, isolated Release build, strict
  OpenSpec validation, `git diff --check`, and record the final validation
  report.
- [ ] Keep real GPU/window and multi-day user acceptance explicitly separate
  from automated evidence.
