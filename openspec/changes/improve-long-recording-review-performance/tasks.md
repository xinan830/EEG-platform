# Tasks

## Phase 0 - Evidence Before Behavior Changes

- [ ] Record the current revision, test environment, and review performance
  matrix for 10 s, 10 min, 1 h, 24 h, and a multi-day fixture.
- [x] Add opt-in diagnostics for navigation targets, raw reads,
  filter/checkpoint source, frame publication, and SciChart binding. Each
  sample carries recording and review-session identities, revision, absolute
  time range, source, point count, and elapsed time without raw signal values.
- [x] Extend the evidence path to algorithm result lookup/computation. Full-run
  metric adaptation, structured-preview cache, indexed `window-preview`, and
  on-demand fallback now emit distinct opt-in sources and timings.
- [x] Add request-count aggregation and bounded memory observations.
  The summarizer aggregates normalized HTTP routes and separates computation
  sources; opt-in 10-second process/dispatcher probes are implemented and
  tested. Actual resource curves remain unverified in the existing manual logs.
- [x] Distinguish `window-preview` read hits from actual filter or algorithm
  execution in logs and tests.
- [ ] Measure continuous playback for unbounded DataSeries growth, dispatcher
  cost, stale response publication, and repeated warmup calls.
  The 2026-10-18 session includes a 22-minute continuous-playback segment with
  stable observed point counts and frame-build timings. Foreground obsolete
  completion/error and checkpoint-progress fencing now have deterministic
  automated coverage; dispatcher/memory curves remain unverified.
- [x] Write the Phase 0 validation report with evidence and a root-cause
  decision before introducing a display-index implementation.

## Phase 1 - Bounded Current Review Path

- [x] Add regression tests for requested/loaded/published identity and obsolete
  response fencing.
  Seventeen deterministic cases cover foreground frame/error/finally paths,
  disposal, cached return, queued drag targets and pending playback reuse.
  Twenty-two additional cases cover checkpoint lifetime, shared-source progress
  subscribers, queued UI callbacks and the real HTTP adapter with synthetic
  responses. Source loading is extracted without creating a second revision
  owner. See `docs/validation/2026-10-18-review-publication-fencing.md`;
  these tests are not GPU or multi-day resource acceptance.
- [x] Keep visible chart points, frame cache, in-flight work, and prefetch within
  explicit budgets; preserve gaps and units. Raw/frame payload retention is
  capped at 64 MiB each; at most two filtered source builds are admitted per
  review loader. Oversized entries are not retained and builds await cancellable
  admission; no samples are truncated.
- [x] Keep waveform rendering independent from algorithm-detail refreshes and
  avoid rebuilding unchanged trace data. The waveform canvas only rebuilds trace
  arrays when frame identity or display geometry changes; PSD uses a separate
  chart host. Existing refresh and binding tests cover the identity guard.
- [x] Validate forward/reverse drag, rapid target changes, playback, cold seek,
  cached seek, filter changes, and recording gaps through deterministic WPF
  tests. This is automated interaction coverage, not a replacement for a real
  GPU/window acceptance pass.

## Phase 2 - Multi-Resolution Waveform Display

- [x] Define the level-selection and bucket/extrema contract from viewport
  pixels, time range, sampling rate, channel count, and source identity. The
  selector and chart-neutral projection are covered by deterministic tests.
- [ ] Implement chunked, indexed, disposable display-level storage with atomic
  publication and explicit gap/invalid-sample handling.
  - [x] Add versioned atomic chunk storage outside immutable recordings, with
    channel-aware keys, bounded bucket validation, and corrupt/truncated cache
    misses.
  - [x] Add pure extrema/gap generation from projected display segments,
    including partial-bucket gap detection.
  - [x] Add atomic level metadata and a bounded reader that rejects missing,
    non-contiguous, or identity-mismatched chunks.
  - [x] Add a chart-neutral trace adapter that preserves extrema sample time
    and emits gap breaks without changing scientific values.
  - [x] Add an atomic publisher that exposes the index only after every chunk
    is complete, with cancellation coverage.
  - [x] Define stable raw/filtered display-source identities from recording,
    montage, filter settings, and filter-contract facts.
  - [x] Add an identity-checked optional display trace provider so SciChart
    consumes complete display levels and falls back on missing or incompatible
    data.
  - [x] Add streaming all-channel publication so level generation retains only
    bounded source chunks and index metadata.
  - [x] Add a persistent level index and wire visible-level reads into SciChart.
    `ReviewDisplayLevelProvider` is an optional hit path; missing, stale, or
    incomplete levels fall back to the bounded review frame path.
- [ ] Build filtered levels only from compatible filtered source chunks; keep
  raw and filtered display identities separate.
- [ ] Add overview/detail zoom tests and bounded-memory/read tests for long
  recordings.

## Phase 3 - Full-Result Reuse And Completion

- [x] Route complete full-analysis windows through indexed read before the
  on-demand producer, preserving the existing scientific identity and
  provenance.
- [x] Add regression tests proving no duplicate filter warmup or algorithm
  computation for a compatible completed window.
- [x] Add fallback tests for missing, stale, incompatible, corrupt, and partial
  artifacts.
  Covers real sidecar/NPZ damage and structured HTTP failures, Run/recording/
  cursor/version mismatches, truncated arrays and ordered window metadata;
  valid scientific Partial windows remain usable, rejected values remain null.
- [x] Run backend tests, focused/full WPF tests, isolated Release build, strict
  OpenSpec validation, `git diff --check`, and record the final validation
  report. The current increment records 766 backend tests and 807 WPF tests
  passing; real resource/GPU/multi-day acceptance remains separate.
- [x] Keep real GPU/window and multi-day user acceptance explicitly separate
  from automated evidence.
