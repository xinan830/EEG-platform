# Smooth live filter handoff and 0.3-second erase band

## Why

Creating and warming a replacement causal filter on the active display path can
pause waveform delivery, especially when high-rate raw context is serialized as
JSON. The sweep also uses a fixed-pixel white line plus a blue cursor, so its
erase gap does not represent a stable time duration across window sizes.

## What changes

- Prepare replacement Python filter sessions in parallel while the active
  filter continues to provide display batches.
- Transfer contiguous post-request raw batches to the replacement session and
  activate it only after it catches the active stream at a sample boundary.
- Transport non-displayed warm-up values as binary float64/V without returning
  a filtered warm-up waveform.
- Render a white 0.3-second cyclic erase range and remove the blue cursor line.

## Scope

This changes display behavior only. Device acquisition, raw float64/V values,
sample counters, recording persistence, and offline analysis are unchanged.
