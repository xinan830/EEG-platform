# Design

## Evidence-First Phases

### Phase 0: Trace the current path

Instrument the existing review path at stage boundaries:

```text
navigation target
  -> request/coalescing
  -> raw read
  -> filter/checkpoint work
  -> display montage projection
  -> frame publication
  -> SciChart binding/render preparation
```

Each measurement carries `recording_id`, review session identity, requested
absolute sample/time, visible range, channel count, filter/montage identity,
request identity, result source (`full`, `on_demand`, `filtered_cache`, or
`raw_fallback`), point count, and elapsed time. It must not log raw EEG values,
credentials, or entire response payloads.

The first phase must explicitly distinguish a cheap `window-preview` read from
actual algorithm/filter execution. A request name alone is not evidence of a
recomputation.

Opt-in resource observations run every 10 seconds while a review view is loaded.
They sample whole-process working set, private bytes, non-forcing managed-memory
estimates, last-GC heap size, and collection counts away from the UI thread. A
single Background-priority dispatcher probe measures queue delay, not GPU time
or total dispatcher CPU cost. Only one probe may be outstanding; unload cancels
the wait and stops sampling. Resource and signal payloads remain separate.

The offline log summarizer groups stage/source timings and resource samples by
review session, and memory by process identity. HTTP and warning/error totals
cover the observation's wall-clock interval because existing HTTP records lack
a causal review-session field. Missing samples are unverified, not evidence of
zero resource usage; no automatic leak or rendering-pass conclusion is inferred.

### Phase 1: Bound the current renderer and scheduler

Keep three positions separate:

- `requested`: the latest user target;
- `loaded`: the last complete frame that is safe to display;
- `published`: the frame currently bound to SciChart.

The navigator may follow the pointer immediately. The review session retains the
last complete frame while a cold target loads, and only a current-identity,
complete frame can replace it. Latest-only scheduling, in-flight deduplication,
bounded cancellation, and a finite frame/data budget are required.

User drag targets fence the previous foreground owner immediately, before the
throttle starts the replacement load. Playback cache probes retain their
existing reuse semantics and do not invalidate a useful pending block on each
clock tick. Each foreground operation owns and disposes its cancellation source;
superseding or disposing the session cancels it without disposing another
operation's source. Obsolete success/failure/finally paths cannot publish changes
or clear the latest loading state. The last complete frame keeps its recording,
sample-counter, time and unit identity while the new requested viewport loads.

The first foreground race fix retained the 705-line session while adding
deterministic publication tests. The next step extracts raw/filtered source
loading, checkpoint coordination and shared chunk ownership into a source
loader, preserving the session as the only foreground revision/frame owner.
The original 10-second chunk keys, filter paths, cache budgets and math remain
unchanged; no new scientific mode or worker pool is introduced.

Progress belongs to shared source work, not to a global coordinator event.
Each foreground waiter subscribes only while awaiting that work, with a callback
guarded by its cancellation token, session revision and lifetime. Replaced
waiters detach; a compatible new waiter may reuse work and its latest progress.
Prefetch has no foreground subscriber. Coordinator close cancels admitted work
and suppresses publication immediately, but defers semaphore/token disposal
until all admitted operations exit, including cancelled queued waiters. It does
not synchronously wait for a non-cooperative reader or remote operation.

The visible chart must not append every playback tick to an unbounded series.
Playback advances the clock and reuses a bounded frame until a cache boundary
is crossed. Waveform rendering has priority over algorithm-detail updates.

### Phase 2: Multi-resolution waveform display

The Phase 2 contract defines `ReviewDisplayLevelSelector` and a disposable chunk
format. The selector chooses a
level from the visible absolute time range, sampling rate, plot width, channel
count and source identity. The target point budget is at most two extrema per
pixel, capped at 2,000 points per trace; the selected samples-per-bucket is the
smallest power of two that satisfies that budget. The level identity includes
source fingerprint, geometry and scientific sampling facts, so raw and filtered
products cannot collide.

`ReviewDisplayLevelChunkCache` now provides versioned, atomic, completed-only
storage outside immutable recordings. A chunk key includes the level identity,
chunk index and channel index. `ReviewDisplayLevelChunkGenerator` consumes
already projected V/float64 display segments and emits ordered extrema or an
explicit gap when a bucket is incomplete or contains invalid samples. This is
storage/generation groundwork only. `ReviewDisplayLevelIndex` and
`ReviewDisplayLevelReader` now validate contiguous coverage and chunk
fingerprints before returning a bounded set of completed chunks.
`ReviewDisplayLevelProvider` is an optional SciChart read path: it replaces
chart points only after a complete identity-matching level is read. Missing,
stale, corrupt, or incomplete levels fall back to the bounded raw/filtered
window path.

`ReviewDisplayLevelTraceProjection` is isolated from the chart control. It
converts extrema sample counters to recording-time X coordinates and emits NaN
breaks for gap buckets. It does not apply display montage, filter, unit
conversion, or vertical scaling; those remain explicit chart/display concerns.

`ReviewDisplayLevelPublisher` writes all channel chunks before atomically
publishing the level index. Cancellation or failure before the final index write
leaves only disposable orphan chunks, which the reader cannot discover.

`ReviewDisplaySourceIdentity` keeps raw and filtered display products separate.
Its canonical input includes immutable recording/channel facts, montage
projection identity, and, for filtered products, filter settings plus contract
and checkpoint versions.

The display layer does not use a fixed universal rule such as “one point per
second.” Each bucket preserves temporal extrema in order (at minimum min/max
pairs) and carries a validity/gap state. Recorded gaps and invalid samples
remain breaks; they are never bridged or replaced with zeros.

Display identity includes recording/manifest identity, source channel order,
unit, filter settings and filter-contract version when filtered, montage/display
projection identity, level/bucket geometry, and display implementation version.
Raw and filtered summaries are separate products. A raw overview cannot satisfy
a filtered waveform request.

The eventual index is disposable, chunked, bounded, and atomically published.
Review will read only the visible level and a small bounded neighborhood.
Zooming selects a finer level; zooming out selects a coarser level. The raw
reader remains the scientific source of truth.

### Phase 3: Full-result reuse

Completed full-analysis windows and on-demand windows share the existing
scientific window identity and result artifact contract. Review first resolves a
complete indexed result for the exact identity. If it is present, the request is
a read-only lookup and must not trigger filter warmup or algorithm execution.
Unfinished or incompatible windows use the existing latest-only on-demand path.

This phase does not make every algorithm support a new execution mode; it only
removes duplicate work where a complete, compatible result already exists.

Full-result reuse verifies the completed Run against its owning request before
adapting values. Structured previews must match the Run/artifact identifiers and
scientific version; their ordered window metadata and per-window array geometry
must agree. Validate the bounded structured cache once when loading, not by
rescanning every numerical cell on each cursor update. Indexed responses must
match the requested Run, recording, cursor and version, and may not select a
future or invalid sample range. Invalid structured caches try indexed read;
unavailable indexed artifacts use the explicit on-demand fallback. No client
checksum or scientific-identity recomputation is introduced.

Missing, stale, incompatible, corrupt and interrupted sidecars are cache misses,
not successful empty output. Backend integrity failures remain structured. This
must not be confused with a scientifically valid `Partial` warm-up window:
usable partial windows retain their backend state/provenance. Rejected or
unavailable scientific windows remain null-valued and are not skipped in favor
of an older valid window or recomputed to hide a quality failure.

## Filter Semantics

Checkpoint behavior remains filter-specific. Stateful causal filters may resume
from a compatible state checkpoint. FIR paths may use a validated overlap
contract. Whole-recording or zero-phase paths must preserve their existing
boundary semantics and cannot claim exact parity from an arbitrary short seek.
Any change to those semantics requires a separate scientific OpenSpec change and
pointwise numerical evidence.

## Resource And Scheduling Limits

The current-path increment retains the raw/frame LRU count limits (2/3) and
adds separate 64 MiB numeric-payload retention budgets. Payload accounting uses
the actual float64 array lengths, conservatively counting shared arrays in each
entry. An oversized entry is returned intact to its caller but is not retained;
no scientific samples are dropped. These are cache payload budgets, not a
whole-process memory cap: active frames, read buffers, chart copies and object
overhead remain outside this accounting.

Each source loader admits at most two filtered chunk builds, including cancelled
but unfinished builds. Identical reusable work bypasses admission and is shared.
Other callers await admission with their foreground and loader lifetime tokens;
cancelled waiters do not allocate raw/filter buffers or start a build. A slot is
released only when the actual build ends, not when a subscriber stops waiting.
Close cancels admission without waiting for non-cooperative builds. This does not
hard-kill remote work or bound arbitrary external callers; the UI retains its
single pending navigation, playback-load and prefetch owners. Filtered disk-cache
retention and all-path transient allocation budgets remain separate work.

Interactive foreground work has priority over prefetch and full-analysis
background work. Worker count, memory, in-flight requests, frame cache size, and
display-index generation are bounded. A newer user target can cancel obsolete
work, but shared completed artifacts are never deleted because one subscriber
changed its target.

## Alternatives Rejected

- Slowing or blocking the mouse/OS cursor: hides latency, harms precise seeking,
  and creates pointer/plot divergence.
- Rendering every raw sample for the entire recording: exceeds chart and memory
  budgets and does not improve scientific fidelity at overview scale.
- Treating every `window-preview` request as recomputation: confuses the API
  name with its execution source and can lead to an unnecessary rewrite.
- Adding unlimited workers: moves the bottleneck to CPU, memory, or disk and
  can make foreground review less responsive.
