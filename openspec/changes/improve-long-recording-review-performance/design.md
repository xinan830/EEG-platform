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

### Phase 1: Bound the current renderer and scheduler

Keep three positions separate:

- `requested`: the latest user target;
- `loaded`: the last complete frame that is safe to display;
- `published`: the frame currently bound to SciChart.

The navigator may follow the pointer immediately. The review session retains the
last complete frame while a cold target loads, and only a current-identity,
complete frame can replace it. Latest-only scheduling, in-flight deduplication,
bounded cancellation, and a finite frame/data budget are required.

The visible chart must not append every playback tick to an unbounded series.
Playback advances the clock and reuses a bounded frame until a cache boundary
is crossed. Waveform rendering has priority over algorithm-detail updates.

### Phase 2: Multi-resolution waveform display

The display layer selects a level from the visible absolute time range, sampling
rate, plot width, and channel count. It does not use a fixed universal rule
such as “one point per second.” Each bucket preserves temporal extrema in order
(at minimum min/max pairs) and carries a validity/gap state. Recorded gaps and
invalid samples remain breaks; they are never bridged or replaced with zeros.

Display identity includes recording/manifest identity, source channel order,
unit, filter settings and filter-contract version when filtered, montage/display
projection identity, level/bucket geometry, and display implementation version.
Raw and filtered summaries are separate products. A raw overview cannot satisfy
a filtered waveform request.

The index is disposable, chunked, bounded, and atomically published. Review
reads only the visible level and a small bounded neighborhood. Zooming selects a
finer level; zooming out selects a coarser level. The raw reader remains the
scientific source of truth.

### Phase 3: Full-result reuse

Completed full-analysis windows and on-demand windows share the existing
scientific window identity and result artifact contract. Review first resolves a
complete indexed result for the exact identity. If it is present, the request is
a read-only lookup and must not trigger filter warmup or algorithm execution.
Unfinished or incompatible windows use the existing latest-only on-demand path.

This phase does not make every algorithm support a new execution mode; it only
removes duplicate work where a complete, compatible result already exists.

## Filter Semantics

Checkpoint behavior remains filter-specific. Stateful causal filters may resume
from a compatible state checkpoint. FIR paths may use a validated overlap
contract. Whole-recording or zero-phase paths must preserve their existing
boundary semantics and cannot claim exact parity from an arbitrary short seek.
Any change to those semantics requires a separate scientific OpenSpec change and
pointwise numerical evidence.

## Resource And Scheduling Limits

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
