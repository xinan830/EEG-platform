## Why

The current topomap rescales colors from each playback window, so identical colors can represent different values as playback advances. Stable per-unit scales are needed for users to compare topomap colors over time.

## What Changes

- Fix relative-power color mapping to the declared `0–1 ratio` domain.
- Calibrate absolute-power color bounds from the first valid window for each RBP run and band, then keep those bounds stable for that run.
- Clamp out-of-range values to the end colors while keeping displayed bounds explicit.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `analysis/eeg-topomap`: Define stable color-scale semantics for relative and absolute RBP topomaps.

## Impact

Affected code is limited to the WPF topomap presentation and its tests. No API, stored result, algorithm formula, scientific value, or dependency changes. Existing API results remain readable; only color projection and legend bounds change. Absolute-scale bounds are visual calibration from the first valid window, not a scientific threshold and not clinical validation. Values beyond those bounds saturate at the palette ends.
