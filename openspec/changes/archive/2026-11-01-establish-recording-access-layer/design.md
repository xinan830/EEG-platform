## Context

The current Review path injects `IRecordingReviewReader` directly and the local
raw reader owns manifest parsing, chunk indexing, gap loading and sample reads.
Acquisition separately owns `SampleBatchRingBuffer` and raw persistence. The
existing playback time coordinator synchronizes cursor state, but it is not a
data source and must stay free of I/O, filtering and algorithm work.

## Goals / Non-Goals

**Goals:**

- Establish one typed source contract for historical and future live recordings.
- Make sample coordinates, channel order, units, gaps, identity and cancellation
  explicit at the boundary.
- Let one workspace share a validated recording session across Review and
  algorithm adapters without sharing their loading or presentation state.
- Migrate the first consumer (Review) without changing scientific calculations.

**Non-Goals:**

- No new filter, montage, algorithm, cache policy or backend API.
- No full-recording preprocessing and no scientific computation in WPF.
- No live ring-buffer adapter until retention and eviction behavior is specified
  and tested against this contract.
- No compatibility branch that keeps the old reader injection path in
  production after migration.

## Decisions

### 1. Put the contract in a recording-owned desktop module

Create a small `Modules/Recordings/Access` boundary for session metadata,
sample ranges, segments, errors and the session manager. Historical local-file
code remains an adapter; acquisition remains the owner of device state, raw
writing and its live buffer. This prevents Review from becoming the owner of a
contract needed by acquisition and algorithms.

An alternative was to put the interface in `Shared`; that would make a
business-specific EEG source contract look like UI infrastructure. Putting it
under Review would make acquisition depend on the viewer. Both directions are
rejected.

### 2. Make integer sample counters the only read coordinate

The public desktop boundary uses half-open integer sample ranges. Seconds are a
display and navigation projection owned by the time coordinator or adapter, so
there is one floor/ceiling policy at the source boundary and no repeated float
rounding across consumers. Values remain float64 in their declared source units;
the local EEG recording contract is V.

An alternative was to expose seconds because Review currently does so. That
would preserve the existing ambiguity between file time and window time and
make the live adapter invent a second conversion policy.

### 3. Share session lifetime, not consumer state

`RecordingSessionManager` keys sessions by recording identity plus source
revision. It reuses the immutable local index/reader and coordinates disposal,
but Review and algorithm adapters keep separate cancellation, cache and
publication state. The time coordinator remains a separate owner of the shared
playback cursor.

An alternative was one global reader per page. That is the current coupling and
allows duplicate indexing, competing lifetimes and inconsistent source
metadata.

### 4. Migrate by replacing the injection boundary

The local adapter will implement the new access session and Review's
`ReviewSourceLoader`/`RecordingReviewSession` will consume it. The old
`IRecordingReviewReader` production path is deleted in the same migration; test
fakes are updated to the new contract. Existing raw manifests and chunks are
read as-is, so historical recordings remain available without a legacy runtime
branch.

### 5. Keep live access as a separate, later adapter

The live ring buffer can implement the same metadata and segmented-read contract
once its retention floor, eviction error, and gap behavior are documented. It
must not be made to look like an immutable recording: an evicted range is a
structured unavailable-range result, not a fabricated gap or a re-read from a
file.

## Risks / Trade-offs

- **[Risk]** Moving Review from seconds to sample ranges can expose rounding
  differences at window edges. → Keep one adapter-level conversion policy and
  assert requested/actual counters in contract tests before changing chart code.
- **[Risk]** A shared session can accidentally become a global mutable cache. →
  Share only immutable metadata and reader lifetime; keep caches and publication
  revisions in each consumer.
- **[Risk]** Existing files may have persisted gaps or incomplete trailing
  chunks. → Validate the manifest/index during open and return explicit gap or
  source errors; never hide them with zero padding.
- **[Risk]** A live adapter may be added before its eviction semantics are clear.
  → Keep it out of this implementation and require a separate contract test
  before registration.

## Migration Plan

1. Add the recording-access contract and focused tests for identity, sample
   ranges, units, channels, gaps, cancellation and failure codes.
2. Wrap `LocalRawRecordingReader` as the first adapter and move session/index
   lifetime ownership into the recording session manager.
3. Change Review composition and cache loading to accept the new session; remove
   the old production reader interface and its injection path.
4. Build the desktop client, run the affected Review/Acquisition contract tests,
   run strict OpenSpec validation and inspect `git diff --check`.
5. Rollback is a source-level revert of this change before the old injection
   path is removed. After migration, a failed adapter is surfaced as an error;
   there is no runtime fallback to a second reader.
