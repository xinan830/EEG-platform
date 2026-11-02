## Why

Review currently receives a concrete `IRecordingReviewReader`, while acquisition
keeps its live ring buffer and raw writer behind unrelated contracts. That makes
the recording source part of each consumer's implementation and prevents the
same recording session from being shared cleanly by Review, TNK, and future
algorithm panels. We need one typed access boundary before adding more
workspaces or readers.

## What Changes

- Introduce a desktop recording-access contract with a stable recording identity,
  revision, sample-rate/channel/unit metadata, explicit gaps, and integer sample
  coordinates.
- Add a recording session manager that owns one access session per recording,
  reuses the session's index/reader lifetime, and provides cancellation-safe
  bounded window reads.
- Adapt the existing WPF local raw-recording reader to the contract without
  moving scientific filtering or algorithm computation into WPF.
- Define the live ring buffer as a future adapter of the same contract; the first
  implementation remains the immutable local-recording adapter.
- Migrate Review's source-loading boundary to the access contract and remove its
  direct dependency on the concrete local reader.
- Do not retain a second production read path or silently translate failed reads
  into empty/zero data.

## Capabilities

### New Capabilities

- `desktop/recording-access`: Typed recording sessions and bounded sample-window
  reads shared by Review, algorithm adapters, and future live sources.

### Modified Capabilities

- None. Review behavior remains the same at this stage; only its source boundary
  is replaced by the new contract.

## Impact

- Affected WPF modules: `Modules/Review/Playback`, `Modules/Review/Cache`, and
  `Modules/Acquisition/Runtime` for the future live adapter.
- Existing local recording manifests and raw chunks remain the source format;
  historical WPF recordings continue to be readable through the new adapter.
- This is an internal breaking change for desktop reader injection: consumers
  must use the recording-access session instead of `IRecordingReviewReader`.
  No legacy production adapter or compatibility branch will remain after
  migration.
- Scientific values stay float64 in volts, sample time remains counter-based,
  channel order comes from the manifest/stream metadata, and explicit gaps are
  preserved. No filter, montage, or algorithm result semantics change here.
- Migration is staged by replacing the Review injection boundary first, then
  adding the live adapter only after its ring-buffer retention and eviction
  semantics are specified. The change does not claim hardware acceptance.
