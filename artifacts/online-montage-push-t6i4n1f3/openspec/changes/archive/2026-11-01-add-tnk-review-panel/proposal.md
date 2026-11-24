## Why

Review currently exposes standalone algorithms as one-at-a-time panels, while the TNK group is available only through its separate algorithm workspace. TNK requires ordered stateful window replay, so treating it as another latest-window scalar panel would omit required history transitions and leave its group chart incomplete.

## What Changes

- Add TNK brain-metric group as a Review algorithm panel using Review's registered recording and time coordinator.
- Map the five required roles `Fz`, `Pz`, `Oz`, `F3`, and `F4` to distinct source channels and preserve the selected montage identity.
- Opt into the existing ordered TNK playback-event contract and close the panel-owned delivery session on panel switch, close, context change, or backward seek.
- Preserve all backend state transitions while showing chart history only from the cursor captured when the panel opens.
- Reuse TNK result projection and chart presentation; keep unavailable children and delivery gaps explicit.

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `desktop/review-algorithm-link`: Review can register TNK as a stateful ordered-window panel without creating another clock or reader.

## Impact

- WPF Review adapter, five-role configuration, TNK panel presentation, menu/button registration, and focused tests.
- Reuses the existing backend TNK on-demand and ordered-event endpoints; no formula, preprocessing, quality, result schema, or scientific version changes.
- The first visible chart time is the panel-open cursor, while backend replay may process earlier canonical windows to establish state.
- Manual local-recording playback acceptance remains required before this capability is considered fully accepted.
