## Why

The live renderer currently places a bucket's minimum and maximum at one
horizontal coordinate. SciChart correctly draws a vertical envelope between
them, but that creates artificial stepped traces which look unlike a continuous
EEG waveform.

## What Changes

- Retain each decimation bucket's first, minimum, maximum, and last samples.
- Render those retained values in their real chronological order at distinct
  horizontal sample positions.
- Remove duplicate same-X min/max line points.

## Compatibility Impact

Only visible waveform geometry changes. Incoming batches, raw persistence,
filtering, sampling, cursor time, and scientific analysis are unchanged.

## Non-Goals

- This does not smooth, interpolate, average, or alter EEG values.
- This does not remove the existing bounded rendering policy.
