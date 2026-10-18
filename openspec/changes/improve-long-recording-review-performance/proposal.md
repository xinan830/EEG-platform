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

## Compatibility Impact

Existing review APIs, recording manifests, raw chunks, full Runs, on-demand
Runs, and historical artifacts remain readable. New display indexes and caches
are disposable and versioned. A missing, stale, corrupt, or incompatible
display index is a cache miss and falls back to the existing bounded review
path. Existing chart behavior remains available while the new display layer is
introduced behind a validated capability boundary.

## Scientific-Contract Impact

The change is presentation and scheduling focused. It may change how quickly a
window is obtained and which display resolution is selected, but it must not
change sample time, channel order, units, gap semantics, filter phase/boundary
policy, montage projection, algorithm window identity, or provenance. Filtered
waveform summaries must be derived from the same filtered source identity as
the visible waveform; raw summaries must never be presented as filtered data.

## Migration And Rollback

The existing bounded-window reader remains the fallback. New display indexes are
created outside the immutable recording and published atomically. Deleting the
derived index or disabling the capability returns review to the current bounded
window path without changing raw data or historical results.

## Validation

The change requires focused backend/WPF tests, strict OpenSpec validation,
`git diff --check`, isolated Release build, and measured evidence. Automated
tests do not claim GPU or physical-device acceptance. A validation report must
separate code measurements from user-observed desktop behavior.
