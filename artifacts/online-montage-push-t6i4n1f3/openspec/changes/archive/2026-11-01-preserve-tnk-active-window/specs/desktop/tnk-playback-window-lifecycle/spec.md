# desktop/tnk-playback-window-lifecycle Specification

## ADDED Requirements

### Requirement: Cursor updates do not cancel active TNK windows

The desktop TNK playback scheduler SHALL replace only a pending target when a
newer playback cursor arrives. It SHALL allow the active scientific window to
reach a terminal state and publish its result before starting the newest
pending target.

#### Scenario: New cursor arrives during computation

- **WHEN** TNK is observing a window and playback advances to a newer cursor
- **THEN** the active window remains owned and is not cancelled
- **AND** the newer cursor replaces any not-yet-started pending target
- **AND** both terminal results retain their actual backend window ranges

### Requirement: Historical subscriber cancellation is recoverable

When a previously persisted TNK window is terminal `cancelled` with failure
code `WINDOW_NO_ACTIVE_SUBSCRIBERS`, a subsequent observation SHALL request a
new generation through the existing explicit retry contract before exposing the
window as unavailable.

#### Scenario: Revisit a window cancelled by the old scheduler

- **WHEN** the backend returns a cached `WINDOW_NO_ACTIVE_SUBSCRIBERS` window
- **THEN** the observer requests its successor generation
- **AND** a successful successor is delivered as the current window result
