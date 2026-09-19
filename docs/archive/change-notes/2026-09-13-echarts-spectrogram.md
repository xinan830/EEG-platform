# ECharts Spectrogram migration

- Added `SpectrogramChart.vue` using ECharts Heatmap and a single full-image VisualMap.
- Heatmap consumes backend `power_db`; the frontend does not calculate dB or smooth the matrix.
- Tooltip shows channel, time center, reconstructed 4-second window, frequency and `dB re 1 µV²/Hz` value.
- NaN/rejected windows remain in the matrix and render as neutral gaps rather than shifting the time axis.
- The color range uses P5–P95 over all finite values in the current matrix; columns are never normalized independently.
- Existing API, algorithm versions and quality metadata remain unchanged.
