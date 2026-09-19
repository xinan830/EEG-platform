# Design

The display ring buffer caches its immutable snapshot until the next append.
The canvas creates immutable render requests on the dispatcher, while a
single latest-frame worker performs frame construction on the thread pool.
New requests replace pending stale requests instead of creating a backlog.

Frame construction maps raw counters to the existing effective display
timeline, selects only the current and retained previous-page spans, and
aggregates min/max values by screen bucket across batch boundaries. A raw
counter discontinuity always starts a new rendered segment.

The completed frame is applied on the dispatcher using one bulk SciChart
append per channel. Cursor position and time labels come from the same
completed frame, so the cursor never leads received or rendered samples.

