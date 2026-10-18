# Design

## State Ownership

`RecordingPlaybackController` owns the requested sample-time position.
`RecordingReviewViewModel` derives the requested viewport from that position.
Session preparation notifications report loading/status and publish complete
frames; they must not restore an earlier viewport. A cache hit is a new navigation
decision and must invalidate any outstanding foreground revision. The chart axis
follows the requested viewport immediately, even while a cold target frame is
loading, so the playhead and the time readout cannot diverge.

The navigator emits drag start/end events. The view model records whether the
single clock was running, pauses it during dragging, and resumes only that
previously running clock. Losing capture and unloading must end the drag.
The navigator must not call `SetCursorPos`; useful disk/filter requests remain
bounded by the existing latest-target scheduler.

## Rendering

SciChart's actual `xAxis.VisibleRange` is authoritative for playhead and marker
geometry. It follows the requested viewport immediately; the source cache range
must never substitute for the visible axis range. A cold target may temporarily
show no trace data, but the cursor must remain at the requested time until the
complete frame is published.

Trace point rebuilding depends on immutable frame identity, physical display
scale and plot height, not on status/progress, playback time or annotations.
Event/lifecycle overlay code remains chart-local and separate from trace binding.
Temporal extrema use fixed-size stack storage rather than per-bucket LINQ
enumerators; this does not change selected samples or bridge real gaps.

Display montage projection can run on a bounded background task after source
loading. Publication and view-model notifications remain on the caller context.
Prefetched frames retain the filter/montage identity captured at build start.

## Limits

No GPU frame-rate guarantee follows from a unit-test pass. An actual desktop
acceptance run must exercise forward/reverse dragging, cached/cold navigation,
playback, resize, montage/filter changes and recorded gaps. Cold causal-history
preparation is still scientifically necessary and is not removed by this fix.
