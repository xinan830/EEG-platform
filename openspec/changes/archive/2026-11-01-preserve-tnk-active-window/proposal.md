# Preserve active TNK playback windows

TNK playback currently uses a latest-only target queue, but the caller also
cancels the active observation whenever a newer cursor arrives. That turns a
normal cursor tick into backend scientific-window cancellation. The cancelled
window is persisted as a terminal result, so the same scientific identity can
later render as a blank interval.

Change the coordinator contract so latest-only scheduling replaces only work
that has not started. An active TNK window completes and is published; only
the waiting target may be replaced by a newer cursor. Existing terminal
`WINDOW_NO_ACTIVE_SUBSCRIBERS` rows remain immutable and are recovered through
the existing explicit retry path in the observer.

## Scope

- Remove active-work cancellation from `LoadLatestAsync`.
- Keep TNK playback on latest-only pending-target coalescing.
- Preserve the existing retry behavior for historical cancellation rows.
- Add the scheduling rule to the desktop OpenSpec contract.

## Non-goals

- No change to scientific formulas, window policy, cache identity, or result
  values.
- No change to page-close or scientific-context reset cancellation.
- No UI rendering optimization in this change.
