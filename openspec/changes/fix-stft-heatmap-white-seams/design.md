# Design

The Review heatmap intentionally keeps previously rendered series for incremental history. Each refresh creates a new `FastNonUniformHeatmapRenderableSeries`; adjacent texture quads can rasterize a hairline at their shared edge. Keep the existing append model and add the last already-rendered backend column to the first new contiguous tile. This identical-value overlap covers the old tile edge without inventing samples or changing absolute-second coordinates. Do not overlap when the returned data contains a real time gap.

Window results can arrive out of order. The merged preview sorts all backend columns, so the heatmap must track rendered time keys individually rather than discard every column older than the latest rendered timestamp. Use the same 9-decimal time normalization as `StftPreview.Append` when checking rendered columns. Late columns fill only their returned timestamps; they do not extend across missing intervals. For each contiguous new tile, include its immediately preceding already-rendered column so the existing tile edge is covered; do not include a column across a real time gap.

## Compatibility and validation

No API, scientific, unit, channel-order, or persistence changes. Add a WPF chart test that checks the overlapping column's time boundary and exact values, and retain the existing forward-gap test to prove missing history stays empty. Run focused desktop STFT tests and build the desktop project.
