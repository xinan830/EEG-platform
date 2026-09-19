# Make rejected spectral windows visible

- A blank block in the spectrogram or band-power trend is not assumed to be missing recording data.
- Spectrogram quality-gate failures remain at their original time positions and are rendered as neutral gray heatmap cells.
- The band-power trend shades rejected intervals and keeps the rejected samples disconnected from neighboring clean samples.
- Tooltips expose the rejection reason and peak amplitude when the backend provides them.
- The panel reports `clean/total` and the number of rejected windows so PSD quality counts are not confused with spectrogram quality counts.

The `offline-spectral-v3` calculations and the backend quality-gate contract are unchanged. The frontend only visualizes the returned quality metadata.

## ECharts 6 compatibility

- Each Heatmap series has an associated `visualMap`; the rejected-window overlay uses a hidden fixed-color map.
- Deprecated `grid.containLabel` was removed from the ECharts views because ECharts 6 recommends the outer-bounds layout model.
