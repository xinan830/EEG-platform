## Why

The current official RBP algorithm accepts one analysis channel and returns one
five-band relative-power series. A selected historical montage can expose many
validated output channels, but the algorithm list cannot analyze or display all
of those outputs together.

## What Changes

- Extend official RBP to analyze every available output in the selected
  historical analysis montage while preserving montage order and identity.
- Return channel-aware static, full dynamic, and on-demand window results.
- Add a desktop RBP history projection with one 100% stacked five-band chart per
  selected montage output. New completed windows append by absolute window end
  time; stale, duplicate, failed, and incompatible-context windows are not
  rendered as valid values.
- Preserve the existing single-channel request path as an explicit fallback
  when no analysis montage is selected.

## Impact

This is a scientific result-contract change. It requires a new RBP scientific
version and implementation identity, updated current Definition metadata,
backend and WPF contract tests, and OpenSpec validation. Raw recordings remain
immutable. The source remains the validated recording montage projection; the
client does not recompute PSD or band power.

