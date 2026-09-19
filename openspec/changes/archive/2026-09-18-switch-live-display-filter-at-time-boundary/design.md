## Decision

At a user setting action, C# snapshots the last accepted raw sample counter and
sets the effective boundary to its successor. The bounded dispatcher filters
all earlier queued batches through the old Python session. On reaching the
boundary, it creates the next Python session, sends only prior contiguous raw
samples as warm-up, discards warm-up output, and sends boundary-and-later
samples to that next session.

The raw writer remains before display and analysis dispatch. No raw batch,
persisted chunk, SQLite record, or scientific artifact is modified.

## Alternatives Rejected

Whole-window re-filtering was rejected because it changes historical display
meaning and can expose an incomplete replacement as blank waveform regions.
Crossfading filter outputs was rejected because it produces values from neither
configured filter and would obscure the exact setting boundary.

## Failure And Race Behavior

If a raw gap separates warm-up from the boundary, no warm-up state is carried
across it. If a newer user action arrives before a pending switch activates, the
pending switch is superseded; only the latest future boundary is rendered. A
filter transport failure may remove filtered display availability but must not
interrupt raw acquisition or persistence.

## Verification

Desktop tests cover sample-major batch slicing and trace segmentation at the
boundary. Backend tests cover filter state, V units, non-EEG preservation, and
warm-up processing.
