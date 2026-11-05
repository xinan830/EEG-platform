## Why

Review users need numerical inspection of existing PSD and RBP results by montage output and frequency band.

## What Changes

- Add a review panel that displays existing PSD frequency samples and RBP band powers for the current playback window.
- Derive band RMS amplitude from the RBP absolute power in the backend, preserving units and quality.
- Reject mismatched or unavailable source windows instead of presenting stale or fabricated values.

## Capabilities

### New Capabilities

- `desktop/review-spectral-values`: Numerical inspection of the existing spectral algorithm results.

### Modified Capabilities

None.

## Impact

Review WPF presentation and dynamic-window coordination, backend RBP output and tests. Existing scientific algorithms and recording data remain unchanged. No migration or new dependency is needed; this is an engineering inspection tool, not a clinical conclusion.
