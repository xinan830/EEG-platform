## Context

See proposal.md - Why. TNK playback calls the desktop `LoadLatestAsync` path. The backend TNK group window capability already replays canonical 4 s windows at 1 s steps from a compatible checkpoint and stores checkpoints while replaying, but a target response currently exposes the target result rather than all replayed point results. The desktop has a bounded sequential queue as well as a latest-target path. TNK timeline charts append ordinary series when the old prefix is unchanged, while stacked series currently replace their data series. These facts mean the pilot must distinguish state continuity, result-history coverage, and rendering cost.

The backend remains the only scientific authority. This change must preserve the current five-role channel mapping, V/float64 internal signal contract, continuous preprocessing, algorithm identities/versions, actual sample windows, quality gates, and module state semantics. The Viewer pipeline and raw recording storage are outside this design.

## Goals / Non-Goals

**Goals:**

- Deliver TNK canonical frame results incrementally to an active review session, including intermediate frames computed during state replay.
- Keep playback clock/render work independent from slow or unavailable analysis work.
- Preserve deterministic state replay and protect the desktop from stale, duplicate, out-of-order, or cross-session results.
- Bound queued work, event buffering, retained history, and chart projection cost; report explicit gaps and result lag when limits are reached.
- Measure playback and analysis stages separately before enabling any cross-window numeric reuse.

**Non-Goals:**

- Live acquisition analysis or real-time clinical feedback.
- Changes to TNK formulas, scientific versions, preprocessing, units, channel mapping, window length/step, warm-up, or quality decisions.
- Converting every algorithm to streaming or replacing backend science with desktop calculations.
- Replacing the transport with WebSocket/SSE solely because the feature is called streaming.
- A new durable archive of every temporary playback event; existing immutable window artifacts remain the reconciliation source where available.

## Decisions

### 1. Treat streaming as ordered window events, not a continuously mutating scientific result

Each event represents one completed canonical TNK frame and carries its existing immutable window identity plus actual half-open sample bounds, recording-relative time, session/configuration identity, implementation identity, quality, failure/evidence, and units. A monotonic delivery sequence is transport metadata only; it does not replace scientific window identity. Clients deduplicate and order by the scientific window range. Processing time and playback-cursor time are never substituted for analysis time.

**Alternative considered:** Publish one mutable rolling array or only the newest scalar. Rejected because it obscures which windows were analyzed and cannot represent gaps, quality rejection, or late results faithfully.

#### Verified storage fit (2026-10-27)

The current SQLite model can represent each canonical intermediate frame as a separate immutable window without a schema migration. `DynamicWindowIdentity` carries the full session/configuration/implementation identity plus a half-open sample range and retry generation; `dynamic_analysis_windows` is unique on `(session_id, start_sample, end_sample, generation)`, and each READY row references one immutable checksummed artifact. `latest_windows(session_id)` returns the newest generation for each range in sample order, and exact-window `get_window(window_id)` reads the matching artifact. The existing `POST /api/analysis-windows/ensure` plans a canonical target from the supplied cursor sample, and the same-range ensure returns the existing row/artifact. Thus reconciliation can enumerate canonical cursor positions and resolve each through the existing ensure/read path. Current TNK replay only returns the target result, so intermediate publication is still implementation work; persistence shape is not the blocker. No schema migration is planned.

### 2. Keep state replay sequential; optimize delivery separately

The group capability remains responsible for checkpoint-compatible restoration and ordered replay of every state transition through the target. It will expose intermediate frame outputs produced by that replay to the playback delivery layer. Latest-target coalescing can reduce obsolete requests that have not started, but it cannot skip a transition needed by stateful TNK modules. Stateless modules may share the same frame schedule for consistency; formula-specific shortcuts are out of scope.

**Alternative considered:** Skip intermediate frames for all modules when the cursor advances. Rejected because it can change stateful outputs and makes module-state semantics implicit.

### 3. Separate interactive freshness from history coverage

The interactive projection follows the newest completed window and reports its lag. The same ordered event stream accumulates every frame produced during uninterrupted playback. A finite event/history budget is mandatory. If capacity or connectivity prevents complete delivery, the session reports the affected range and reconciles it through exact-window reads/ensures; it never labels an incomplete timeline complete. Full Runs remain the existing durable complete-coverage mechanism.

**Alternative considered:** Put every cursor target in an unbounded sequential queue. Rejected because slow analysis would create unbounded backlog and could make the displayed algorithm increasingly stale. A bounded queue with silent dropping is also rejected because it hides history loss.

### 4. Preserve the existing transport until measurements justify a transport change

Represent delivery with a resumable, ordered subscription contract. Start by extending the existing window-observation/polling path if its measured delivery latency and request load meet the playback budget. Change to SSE or another push transport only if baseline measurements show polling is a material bottleneck; do not add WebSocket as a presumed compute optimization. Any transport must support close/cancel, resume from sequence, bounded buffering, and explicit gaps.

**Alternative considered:** Introduce WebSocket immediately. Rejected because it adds connection lifecycle and recovery work without reducing scientific computation, and current evidence does not establish transport as the bottleneck.

#### Diagnostic and bounded-history contract

Playback diagnostics are opt-in and metadata-only. A TNK event carries a monotonic delivery sequence, session/window identity, sample bounds, and publication timestamp; diagnostic samples additionally carry recording/session/configuration identity, queue depth and active/newest cursor samples, replayed-frame count, stage duration (`queue_wait`, `source_prepare`, `state_replay`, `compute`, `publish`, `delivery`, `projection`, `chart_update`), latest completed window end sample, current playback sample, and playback tick interval/handler duration. Result lag is computed from the difference between playback sample and the event's actual `end_sample`, divided by the recording sampling frequency; result time is never inferred from publication time. Logs and diagnostic buffers must not contain EEG arrays or parameter payloads.

The resumable event ring retains at most 2,048 complete, single-window event payloads per active playback session. This is necessary because current checkpoint replay persists the requested target window, but not every intermediate frame it computes; retaining only metadata would make a reconnect unable to replay those values. Each event is one TNK metric point with bounded child evidence, while raw EEG arrays remain outside the event contract. When the ring evicts an unacknowledged sequence, delivery reports the first/last missing sequence and affected sample range. The client marks coverage `incomplete`/`reconciling` and enumerates canonical windows through the existing ensure/read path; it may report `complete` only after every expected frame through the acknowledged target is represented, including explicit rejected/unavailable states. The ring size is an initial engineering cap, not a measured latency guarantee; paired desktop measurements must verify memory use, overflow behavior, and recovery before enabling the capability. Diagnostics retain at most 512 playback tick samples per five-second reporting interval and 256 recent samples per other diagnostic stage and emit aggregate summaries every five seconds; they are disabled unless `BRAIN_REVIEW_PERF_DIAGNOSTICS=1`.

### 5. Keep desktop chart work incremental and bounded

The view-model history projection accepts event batches off the playback-critical path and coalesces dispatcher notifications without discarding scientific points. Ordinary line series append only the new suffix. Stacked series will be changed only if paired measurements show their replacement cost materially affects playback; if replacement remains, it must be bounded and must preserve visible-range behavior. Waveform data and its renderer are never rebuilt in response to a TNK result.

**Alternative considered:** Rebuild all chart points after every event. Rejected because its work grows with history length and competes for UI thread time.

### 6. Gate cross-window reuse on independent equivalence evidence

After the event path is measured, profile repeated source preparation and overlapping-window spectra. Only then consider caches or incremental transforms, keyed by source identity, role/channel order, reference, preprocessing contract, filter settings, sample range, scientific version, and configuration. A replay-versus-reference test must cover state reset, sequential playback, backward seek, and configuration change before enabling reuse.

**Alternative considered:** Rewrite every TNK algorithm as a sample-by-sample state machine in this change. Rejected because it broadens scientific risk and is unnecessary to prove incremental result delivery.

### 7. Coalesce preview work before profiling scientific reuse

The 2026-10-27 playback logs showed six concurrent `signal-preview` requests
for one recording, each taking about 15 seconds. The desktop cancelled only its
local wait and immediately started a replacement, while the backend continued
processing the cancelled request. The preview path also entered the continuous
preprocessing cache without an in-flight owner, so simultaneous misses could
each read and filter the recording.

The repair serializes waveform preview refreshes in the desktop, keeps only the
latest pending refresh, selects only requested preview channels, and uses a
single-flight cache for identical prepared-source identities. This changes
request scheduling and resource contention only; it does not change TNK window
boundaries, formulas, units, preprocessing parameters, or numeric reuse across
windows. Paired desktop performance acceptance remains required before the
streamed playback capability is declared accepted.

## Risks / Trade-offs

- [Intermediate event publication increases payload and CPU/memory use] → Batch transport metadata while retaining per-window scientific identity; cap payload/history; include overflow and reconciliation tests.
- [A late subscriber may have missed volatile events] → Resume with a delivery sequence while available and reconcile missing exact windows; expose a structured history-incomplete state if reconciliation cannot finish.
- [Checkpoint identity omits a scientific input] → Reuse the existing complete dynamic-session identity and add tests that vary each source/configuration/version field; never expand reuse based only on cursor equality.
- [Chart append can still scan or copy all prior points] → Measure projection and binding cost at increasing history lengths; optimize prefix tracking and stacked-series handling only against that evidence.
- [Performance results vary by machine and recording] → Keep deterministic stage timers in automated tests and document real-desktop paired measurements separately; do not convert one machine's result into a universal latency promise.

## Migration Plan

1. Add the event contract and backend replay publication behind an opt-in TNK playback capability; retain the current target-only observer as fallback.
2. Integrate desktop ordered history, stale-session rejection, explicit lag/coverage, and bounded chart updates.
3. Before enabling the new path, capture paired waveform-only and current waveform-plus-TNK baselines with the same local recording, playback speed, selected roles, and plotting configuration; set measurable thresholds from those results.
4. Validate stateful parity, transport load, and navigation/cancellation cases; repeat the paired performance measurements and complete manual desktop acceptance.
5. Enable the new path only after the baseline thresholds and acceptance checks pass. Rollback by disabling the capability and returning to target-only observation; no raw recording or historical Run migration is required.

No schema migration is planned. Before implementation, verify that the existing immutable window identity and repository can safely publish/reconcile every intermediate canonical frame. If that requires a persistence schema change or unbounded retention, stop and revise the proposal before coding.

## Open Questions

- Whether the existing polling observer meets the measured event-delivery budget; answer this after baseline and before choosing any push transport.
- The default in-memory history duration and event-buffer size; set them from representative recording length and memory measurements, with explicit gap/reconciliation behavior at the limit.
