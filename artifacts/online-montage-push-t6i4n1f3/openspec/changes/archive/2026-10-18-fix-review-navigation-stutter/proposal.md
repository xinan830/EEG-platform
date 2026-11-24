# Fix review navigation stutter and cursor rollback

## Why

Review navigation can restore an obsolete viewport on preparation notifications,
publish an old foreground frame after a newer cached seek, and position the
playhead against the full cache block instead of the visible chart axis. The
navigator also moves the operating-system pointer to enforce a drag speed cap.
Repeated frame notifications rebuild unchanged SciChart data at UI cadence.

## What Changes

- Keep the requested playback position and viewport authoritative during loads.
- Fence older loads when a newer target is satisfied by the frame cache.
- Follow thumb input directly without moving the operating-system pointer;
  throttle/coalesce loading instead of rate-limiting pointer movement.
- Suspend the single playback clock during a thumb drag and restore its prior
  running state when the drag ends or loses capture.
- Position overlays using the actual visible axis and reuse unchanged trace
  data for status, time-range and annotation updates.
- Prepare display montage projection away from the UI dispatcher, preserving
  the existing formula, recorded sample order, V values and explicit gaps.

## Scope And Compatibility

Only WPF review interaction, frame scheduling and display rendering change.
Raw recordings, backend filtering, algorithm mathematics, analysis provenance,
trial/licensing, and default-off on-demand analysis switches do not change.
Cold exact causal filtering may still require recorded history; old complete
frames remain visible while this work finishes.

## Validation

Add regressions for stale preparation notifications, cached seeks superseding
pending work, bidirectional dragging, playback ownership, actual-axis cursor
geometry, and unchanged SciChart series reuse. Run review/full WPF tests, Release
build, strict OpenSpec validation, file-size checks and diff checks. Keep physical
mouse/GPU acceptance separate from unit tests.
