# Proposal

## Why

Incrementally appended STFT columns are rendered as separate heatmap textures. Their rasterized edges can expose thin white seams even when backend time columns are contiguous, making valid power data look missing.

## What Changes

- Overlap contiguous appended heatmap tiles by one existing backend column so texture edges are covered by already-rendered data.
- Keep genuine unvisited time gaps and unavailable matrix cells visibly empty.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/stft-review-panel`: contiguous accumulated STFT columns render without artificial seams while genuine gaps remain unfilled.

## Impact

Only the WPF STFT heatmap tile assembly and its desktop tests/specification change. Backend calculations, time/frequency axes, units, and returned data are unchanged; no migration or dependency changes.
