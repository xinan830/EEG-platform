## Why

Changing a live display filter currently replaces the displayed history before
the replacement history is complete. This produces empty screen regions,
filter-start transients, and a misleading discontinuity even while raw
acquisition remains valid.

## What Changes

- Keep the complete prior display window visible while a replacement is built.
- Build a candidate from bounded raw pre-roll plus the whole visible screen,
  then replace the display window atomically.
- Continue the new live filter from the state reached at the right edge of its
  candidate window and reject stale previous-configuration batches.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/acquisition-core`: Require atomic complete-window replacement for
  in-recording display-filter changes.

## Impact

Changes local display-buffer lifecycle and local Python filter-session payloads.
Raw data storage, hardware capture, and scientific analysis runs are unchanged.
