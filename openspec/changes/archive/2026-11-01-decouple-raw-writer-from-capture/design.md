# Design

`AcquisitionCoordinator` wraps each recording writer with a bounded
`QueuedAcquisitionRawWriter`. Batch operations use a non-blocking enqueue path;
control operations (lifecycle, diagnostics, completion, abort) are ordered on
the same channel and may await their own completion. A single worker invokes
the existing `IAcquisitionRawWriter`, so file writes remain serialized.

The queue has a finite capacity. When it cannot accept a batch, the wrapper
raises a `RAW_WRITE_QUEUE_OVERFLOW` failure. The coordinator transitions the
recording to faulted state and retains the counter continuity evidence already
written. The queue never drops a batch to keep the UI alive.

The capture loop still owns sample-counter observation and display buffering.
The raw writer pipeline owns persistence order and drain semantics. This keeps
device acquisition, raw persistence, display, and analysis as separate state
machines without changing the on-disk contract.
