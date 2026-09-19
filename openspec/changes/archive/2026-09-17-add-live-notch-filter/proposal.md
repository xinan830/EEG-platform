## Why

The desktop acquisition page exposes mains-frequency suppression, but it must
be a real, traceable part of the backend-owned display pipeline rather than a
visual-only control. The raw EEG record must remain available for later
scientific analysis without this display-only transformation.

## What Changes

- Add explicit 50 Hz, 60 Hz, and disabled notch choices to the live display
  filter contract.
- Return filtered EEG display batches from the local Python service while
  retaining non-EEG columns and raw persisted values unchanged.
- Keep high-pass, low-pass, and notch settings immutable during a recording.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/acquisition-core`: Extend backend-owned live display filtering with
  an optional validated mains-frequency notch.

## Impact

Changes the local desktop-to-Python live filter API and desktop acquisition
controls. It adds no storage schema, no analysis-Run behavior, and no external
network dependency.
