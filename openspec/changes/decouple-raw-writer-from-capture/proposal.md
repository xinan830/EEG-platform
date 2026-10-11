# Decouple Raw Persistence From Device Capture

## Problem

The acquisition loop currently awaits raw-file persistence before reading the
next vendor batch. At 4 kHz with 30 channels, a write-through flush or storage
latency can stop SDK reads long enough for the device buffer to overflow. The
older recording `aefac2c933ad4e58956992d605f230dc-20261010133224` proves the
failure mode: eight explicit counter gaps totaling 3588 samples (about 2%).

## Decision

Insert a bounded, single-reader raw-write pipeline between the device capture
loop and the immutable raw writer. Capture enqueues batches and continues
draining the SDK. The pipeline preserves operation order and drains before a
recording completes. Queue overflow or writer failure must become an explicit
recording fault; it must never silently discard a raw batch.

## Scope

- WPF acquisition runtime and raw persistence boundary only.
- No changes to EEG values, sample counters, file format, units, or scientific
  algorithms.
- Add deterministic tests for slow writes, ordering, draining, and overflow.

## Acceptance

- A slow raw writer does not make `ProcessBatchAsync` wait for each batch.
- Completion waits until all accepted raw operations are persisted.
- A full queue produces a structured acquisition fault rather than silent loss.
- Existing raw recordings remain readable and their binary format is unchanged.
