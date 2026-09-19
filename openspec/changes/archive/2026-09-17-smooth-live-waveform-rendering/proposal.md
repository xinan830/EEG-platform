# Change: Smooth live waveform rendering

## Why

The live acquisition chart rebuilds and appends the full retained sweep on the
WPF dispatcher every 50 ms. Small vendor batches also defeat the current
per-batch decimation, causing avoidable UI stalls and a visibly uneven sweep.

## What Changes

- Decimate across vendor batch boundaries into screen-density extrema.
- Build the latest waveform frame away from the WPF dispatcher and coalesce
  stale frame requests.
- Submit each channel to SciChart with a bulk append.
- Cache unchanged display-buffer snapshots.

## Non-Goals

- Do not change acquisition, raw persistence, sample-counter, pause, or gap
  semantics.
- Do not interpolate or advance the sweep beyond received device samples.
- Do not change EEG units or scientific processing.

