## Why

A cold jump deep into a long recording currently waits for every preceding
10-second filtered chunk to be built recursively before the target frame can
appear. That preserves exact causal state, but it couples foreground navigation
latency to recording age and gives the user no bounded progress model.

## What Changes

- Persist validated causal checkpoint anchors separately from filtered display
  chunks so later sessions can resume from the nearest compatible earlier
  anchor.
- Generate missing anchors sequentially in bounded background work while
  preserving exact Python-owned causal filtering across contiguous samples.
- Prioritize the current foreground target, coalesce obsolete seek requests,
  expose deterministic progress, and keep the last complete frame visible.
- Reset anchor continuity at every recorded sample-counter gap; never skip raw
  history inside a contiguous segment and never synthesize missing samples.
- Bound cache size and concurrency, use completed-only atomic writes, and make
  all anchor data disposable and rebuildable from immutable raw recordings.
- Preserve acquisition/review lifecycle separation, V/float64 source values,
  recorded stream channel order, filter settings, and existing public filter
  APIs.

Non-goals are approximate filter initialization, truncated warm-up presented as
exact output, loading an entire recording into memory, rewriting raw chunks,
or moving scientific filtering into C#/WPF.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/recording-review`: Add exact, persistent checkpoint-anchor planning,
  foreground/background scheduling, progress, invalidation, gap, and failure
  behavior for long-record review navigation.

## Impact

- Desktop review scheduling, filtered-source cache identity/storage, session
  status, and associated C# tests will change.
- The existing local Python filter checkpoint export/import contract remains
  authoritative; backend changes are limited to compatibility or diagnostics if
  implementation evidence shows they are needed.
- No raw recording schema, public unit, channel ordering, montage contract, or
  acquisition state machine changes.
- Existing derived cache files remain safe to ignore and rebuild. New anchor
  entries use a versioned fingerprint including recording identity, sampling
  rate, source-channel schema, filter settings, Python filter-contract
  fingerprint, segment origin, and sample counter.
- Compatibility risk is limited to disposable local review caches. Scientific
  risk is causal-state reuse across incompatible settings or gaps; strict key
  validation and continuity checks are mandatory.
- Migration is lazy: existing recordings need no conversion, and anchors are
  generated only from immutable raw data as review work requires them.
