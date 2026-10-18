# Complete WPF STFT Detail

## Why

The official STFT runtime already returns static and dynamic structured results,
but the WPF STFT detail view is a placeholder. Its preview parser accepts only
static two-dimensional output, so a dynamic Run has no time-frequency chart.

## Scope

- Give STFT its own result component with a heatmap, axes, unit, quality state,
  and clear unavailable state.
- Project the backend's selected complete dynamic window into the same chart
  and update it when the existing timeline moves.
- Keep algorithm calculations, quality decisions, and provenance in the backend.

## Non-goals

- No STFT numerical-contract changes or new preprocessing parameters.
- No live acquisition or recording-review integration.
- No redesign of the PSD detail page.
