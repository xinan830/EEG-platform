# Design

`DynamicWindowTargetCoordinator` owns one serial observation lane. Its
latest-only mode may discard a pending target because that work has not started.
Once a work item becomes `active`, its cancellation token belongs to the
observation owner and is cancelled only by an explicit reset/context change or
page shutdown. A newer playback cursor cannot cancel it.

This preserves TNK's stateful replay checkpoint progression and prevents the
backend subscription cleanup from converting a valid in-progress window into
`WINDOW_NO_ACTIVE_SUBSCRIBERS`. The next cursor remains in `pending` and is
started after the active window reaches a terminal state. The observer's
existing retry path handles rows already persisted as cancelled by the prior
behavior.

The change deliberately leaves generic page cancellation and scientific
context-reset behavior intact. It also leaves the separate WPF display queue
unchanged; that queue is a latency issue to measure and fix independently.
