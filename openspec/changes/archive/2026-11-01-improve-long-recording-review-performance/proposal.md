# Improve Long-Recording Review Performance

## Why

Reviewing recordings that span many hours or days must remain responsive without
loading the complete raw recording, rebuilding a filter for every pointer move,
or allowing chart data and obsolete asynchronous work to grow without bounds.
The current review path already has bounded source chunks, causal checkpoint
anchors, latest-target loading, and full/on-demand analysis modes. The remaining
work is to measure the real bottleneck before changing the renderer or storage
layer, then add a long-recording display path that is explicit about resolution,
gaps, filter identity, and result provenance.

## What Changes

- Add bounded review-performance evidence for raw reads, filter warmup/checkpoint
  work, window-preview result reads, frame publication, SciChart binding, and
  chart point counts.
- Keep the requested navigation target, last complete frame, and loaded data
  position separate; stale work must not overwrite a newer target.
- Ensure visible trace data and in-flight review work remain bounded by the
  viewport and configured cache budget.
- Add a multi-resolution waveform display layer for long recordings. Its level
  is selected from visible time range, sampling rate, and plot width; it must
  preserve explicit gaps and use the correct raw or filtered data identity.
- Make the waveform architecture explicit: the shared playback time
  coordinator owns play/pause/seek/cursor generations, while the waveform
  request coordinator owns only latest-target loading and cancellation.
- Keep recording access, waveform session state, display processing, frame
  publication, and SciChart rendering as separate owners. Remove any remaining
  internal parallel reader path instead of adding another compatibility layer.
- Give continuous playback and one-off preview independent mutable filter state
  and checkpoints while sharing only immutable recording/filter configuration.
- Make completed full-analysis windows readable through the same indexed result
  path used by review without triggering duplicate scientific computation.
- Record measurable acceptance evidence for 10-second, 10-minute, 1-hour,
  24-hour, and multi-day recordings.

## Non-goals

- No real-time analysis mode.
- No change to PSD, STFT, RBP, FAA, IAPF, Brainbeat, or other algorithm
  formulas, units, window definitions, or scientific reference semantics.
- No change to causal or zero-phase filter meaning without a separate approved
  scientific contract and numerical comparison.
- No rewriting of immutable raw recordings.
- No artificial mouse or OS-cursor movement and no fixed sleep used to imitate
  loading latency.
- No unbounded worker or thread increase.

## Capabilities

### New Capabilities

- `desktop/long-recording-review`: bounded, identity-safe waveform display for
  long recordings, including request coordination and complete-frame publication.

### Modified Capabilities

- `desktop/recording-review`: make the shared time-coordinator, recording-access,
  and display/rendering ownership boundaries explicit.

## Impact

Existing review APIs, recording manifests, raw chunks, full Runs, on-demand
Runs, and historical artifacts remain readable. New display indexes and caches
are disposable and versioned. A missing, stale, corrupt, or incompatible
display index is a cache miss handled by the same canonical bounded
raw/filtered display pipeline; it must not reopen a retired reader API or add a
second internal data path.

## Scientific-Contract Impact

The change is presentation and scheduling focused. It may change how quickly a
window is obtained and which display resolution is selected, but it must not
change sample time, channel order, units, gap semantics, filter phase/boundary
policy, montage projection, algorithm window identity, or provenance. Filtered
waveform summaries must be derived from the same filtered source identity as
the visible waveform; raw summaries must never be presented as filtered data.

## Migration And Rollback

The canonical bounded raw/filtered window pipeline remains the fine-resolution
path. New display indexes are created outside the immutable recording and
published atomically. Deleting a derived index returns review to that same
canonical pipeline without changing raw data or historical results. No retired
reader, old request format, or silent compatibility route is retained.

## Validation

The change requires focused backend/WPF tests, strict OpenSpec validation,
`git diff --check`, isolated Release build, and measured evidence. Automated
tests do not claim GPU or physical-device acceptance. A validation report must
separate code measurements from user-observed desktop behavior.
