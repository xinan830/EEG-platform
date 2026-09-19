## Why

Replacing the entire visible waveform when a live display-filter setting changes
rewrites history that was viewed under the prior setting. It also caused blank
regions when replacement data was incomplete. A filter control action needs one
unambiguous temporal meaning instead: it affects samples after the action, not
earlier samples.

## What Changes

- Switch Python-owned live display filters at an explicit raw sample-counter
  boundary.
- Preserve previous filtered display values and initialize the new causal
  filter with non-displayed contiguous raw pre-roll.
- Break only the rendered trace at the boundary; do not create a time gap.
- Replace the prior whole-window atomic-replacement contract.

## Compatibility Impact

Raw file format, acquisition lifecycle, local HTTP routes, and scientific Runs
are unchanged. This changes only display-filter history semantics during a
live acquisition.

## Non-Goals

- This is not a scientific preprocessing Run or an offline reprocessing mode.
- It does not promise full settling for very-low high-pass cutoffs with bounded
  real-time pre-roll.
- It does not change persisted raw EEG, which remains V/float64.
