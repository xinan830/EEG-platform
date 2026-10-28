## Why

TNK playback currently prioritizes the newest cursor target, while the backend replays intermediate frames to preserve state; only completed target results are delivered to the desktop history. This can leave the chart behind or incomplete even when state replay is correct, and the current behavior has not been measured end to end against waveform playback.

## What Changes

- Add a TNK playback-follow execution path that publishes completed canonical windows incrementally while keeping waveform playback independent of analysis work.
- Preserve stateful TNK window order through checkpoint restore and sequential replay; latest-only may replace an unstarted display target and playback may cancel an active stale replay at a cooperative checkpoint, but the replacement replay must restore a compatible checkpoint and may not omit a scientific state transition or silently erase required timeline results.
- Identify and order results by immutable analysis-window identity and actual half-open sample/time range, with quality, failure, and configuration/session provenance; do not label a late result with the current playback cursor.
- Append returned TNK timeline points in the desktop and keep chart publication work bounded and off the playback-critical dispatcher path.
- Instrument playback ticks, source/window preparation, queue wait, computation, event delivery, projection, and chart update; use paired measurements to identify whether the bottleneck is scheduling, compute, or rendering.
- Keep formulas, units, window policy, preprocessing, quality rules, and scientific versions unchanged. Defer cross-window numeric reuse until per-window equivalence and profiling justify it.
- For the migrated ANT time-series children, share one raw Fz chunk stream per TNK session, retain the distinct Crimson/Zenlite preprocessing contracts, and share Zenlite model inference across calmness, awareness, and spectral relaxation.
- Add opt-in stage timings for the waveform signal-preview request and preserve per-channel display-window minima/maxima alongside the existing uniformly sampled preview line.

## Capabilities

### New Capabilities
- `analysis/tnk-streamed-playback`: Incremental, identity-safe TNK playback results with explicit latest-follow and complete-history semantics.

### Modified Capabilities
- `viewer/signal-preview-integrity`: add bounded, opt-in preview-stage diagnostics and an additive per-channel extrema envelope without changing the existing sample/time fields.
- `analysis/tnk-streamed-playback`: add on-demand STFT time-column history append behavior to the desktop projection.

## Impact

- Backend: TNK group window replay/checkpoint path, bounded event/result delivery, window identity and result serialization; signal-preview stage timing and min/max envelope projection.
- Desktop: TNK playback coordination, owned-window history projection, on-demand STFT column append, signal-preview envelope rendering, and SciChart timeline update path.
- Diagnostics/tests: paired playback-only versus playback-plus-TNK measurements, preview-stage timing, narrow-transient envelope coverage, stateful replay/seek/cancellation coverage, event ordering and history-completeness validation.
- Compatibility: existing full Runs and immutable on-demand window reads remain compatible. No raw recording or database schema migration is intended; if complete-history delivery requires durable intermediate windows, that storage/API decision must be separately justified before implementation.
- Non-goals: live acquisition streaming, formula changes, universal conversion of every algorithm, replacing HTTP polling with WebSocket/SSE as a presumed optimization, and clinical validation.
- Scientific-contract impact: none intended. Analysis time, units, channel-role mapping, preprocessing, window boundaries, quality semantics, provenance, and numerical results must remain equivalent to current TNK execution. The preview envelope is display metadata in µV, indexed by recording-relative display-bin center; it is not a new scientific result or a replacement for raw samples.
- Migration and rollback: opt-in TNK playback path with the existing per-target on-demand path as fallback; disabling the capability restores current behavior without rewriting recordings or historical Runs.
