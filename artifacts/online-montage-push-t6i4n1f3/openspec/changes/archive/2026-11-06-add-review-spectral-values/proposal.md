## Why

Review users need to inspect the numeric PSD and RBP outputs already produced for the current playback analysis window, so they can verify those existing calculation paths directly.

## What Changes

- Add a button to open a right-side numerical panel in recording review.
- Show one row per current montage output and a band selector for Delta, Theta, Alpha, Beta, and Gamma.
- Display the selected band's amplitude, RBP absolute power, RBP relative power, and PSD band-mean density as numeric values with units.
- Drive the existing PSD and RBP dynamic adapters from the same review cursor and reuse their results; derive only RMS amplitude from the matching RBP absolute power.
- Keep playback analysis window and segmentation settings consistent with the existing PSD/RBP paths, and show unavailable values when source results do not match or are missing.
- Expose saved-window scalars independently from spatial rendering through one shared result adapter; numeric reads never interpolate maps, and table updates retain row objects.
- Present row inspection details in a bounded, wrapping tooltip with readable source quality summaries rather than serialized quality JSON; format compact display units while retaining scalar precision and unavailable reasons.

## Capabilities

### New Capabilities

- `desktop/review-spectral-values`: Numerical inspection of existing PSD and RBP results in recording review.

### Modified Capabilities

None.

## Impact

Review WPF presentation and paired bindings to the existing PSD/RBP dynamic adapters; reuse the PSD band-mean-density projection and RBP absolute/relative arrays. Connect the existing backend RMS helper through the current RBP result-projection contract without changing official algorithm outputs. No new scientific algorithm or raw-sample processing path is intended. This is an engineering inspection tool, not a clinical conclusion.

The numeric panel consumes the typed `/api/analysis-windows/{window_id}/scalar-values` resource. Existing topomap consumers retain their spatial endpoint; both use `result_scalar_inputs.py` for the same algorithm result-to-scalar projection. No fallback to topomap, compatibility aliases or stored-data migration is introduced.
