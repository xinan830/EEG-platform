# Design

## Confirmed evidence
The reported recording has finite F8 samples. Existing default 1/30/50 Hz cache
values match a fresh backend filter pass exactly for all 21 REF outputs in its
first twenty seconds. The old chart inserted `(NaN, NaN)` even between
contiguous chunks, and skipped invalid values inside decimation buckets.
An actual SciChart render using these same cached values reproduced the missing
F8 prefix before the fix. Removing invalid X and false chunk breaks restores
F8 without modifying values or disabling the accelerated renderer.

## Display boundary
Use sample counters to identify real discontinuities. Adjacent chunks are one
continuous signal and receive no fabricated break. For a real gap insert a
finite time coordinate with NaN Y before the next valid sample. Within a chunk,
an invalid source value also breaks the line without making time non-finite.
Select first/min/max/last finite values in temporal order within each bounded
bucket. This is display-only reduction, not interpolation or EEG calculation.

## Ownership and size
Extract the trace-point projection into a focused review waveform helper. The
existing canvas owns chart binding, layout, viewport and overlays, not source
filtering. Preserve all unrelated local canvas edits. The extraction reduces
the oversized canvas rather than appending another workflow to it.

## Alternatives and rollback
Deleting caches cannot fix an invalid chart time coordinate and is not justified
by numeric evidence. Changing filter parameters or switching montage changes the
signal and can conceal the renderer symptom; neither is a fix. Disabling GPU
rendering globally would affect performance and requires separate evidence.
Rollback restores the previous display projection only; raw/cache files remain
readable. GPU acceptance remains separate from numerical/contract test success.
