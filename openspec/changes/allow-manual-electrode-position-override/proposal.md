## Why

Operators need to adjust a standard-template electrode's diagram position when the template does not match their intended illustration. The current map blocks dragging any channel that already has a resolved standard location.

## What Changes

- Allow a selected EEG channel with a standard position to be repositioned in the floating head diagram.
- Keep the resolved standard XYZ snapshot intact and persist the adjusted normalized 2D position separately.
- Mark manually adjusted positions as diagram-only overrides.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/electrode-locations`: permit manual 2D diagram overrides for channels with standard-template positions.

## Impact

- WPF channel position-map interaction and its projection tests.
- OpenSpec electrode-location behavior. No API, backend, signal, reference, time, or unit changes.
