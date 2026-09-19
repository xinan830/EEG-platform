## Decision

For each screen-density bucket, retain up to four actual samples: first,
minimum, maximum, and last. Deduplicate when roles identify the same sample,
then sort by sample counter before rendering. Each output point has equal
min/max values, so the renderer produces one chronological polyline instead
of a vertical extrema envelope.

## Trade-off

The point bound increases from one to at most four retained points per bucket
per contiguous trace segment. This remains bounded by screen density and keeps
actual extrema instead of applying a visually smooth but scientifically altered
average.

## Verification

Tests cover chronological extrema selection, distinct horizontal positions,
continuity boundaries, and bounded point counts.
