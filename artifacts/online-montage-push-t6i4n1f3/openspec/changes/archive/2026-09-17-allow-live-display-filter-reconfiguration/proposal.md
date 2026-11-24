## Why

Users need to compare real-time display filters while recording without
stopping a valid acquisition. The prior rule locked controls for the whole
recording, even though these filters never alter raw data.

## What Changes

- Permit high-pass, low-pass, and supported notch changes during recording or
  pause for the live display stream only.
- Initialize a replacement backend filter with recent contiguous raw display
  context and replace the visible filtered history at the configuration change.
- Discard stale display batches returned under the previous configuration.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/acquisition-core`: Replace the in-recording filter lock with a
  versioned, raw-preserving display-filter reconfiguration contract.

## Impact

The local desktop/Python live-filter API gains optional warm-up data. It does
not change raw storage, analysis runs, SQLite, device capture, or external APIs.
