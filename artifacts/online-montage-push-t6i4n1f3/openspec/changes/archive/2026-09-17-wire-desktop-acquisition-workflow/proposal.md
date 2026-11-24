## Why

The desktop shell contains the ANT/eego adapter but starts permanently with an
unavailable adapter. A user therefore cannot configure, discover, open, record,
or stop a real device from the application.

## What Changes

- Add a small local acquisition settings store for the ANT SDK path, explicit
  ranges, and raw-recording directory.
- Add an application-layer runtime that owns adapter replacement and the
  existing acquisition coordinator lifecycle.
- Wire a deliberately minimal WPF workflow: apply configuration, scan, select
  an actual device and rate, start/stop recording, and display returned channel
  metadata and state.
- Stop and dispose an active stream on desktop shutdown.

## Non-Goals

- No polished final UI, mock device data, waveform renderer, impedance,
  triggers, Python analysis ingestion, backend SQLite write, or scientific
  algorithm execution.
