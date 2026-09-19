# ECharts PSD migration

- Added `SpectrumPsdChart.vue` using ECharts 6.
- PSD uses the real frequency bins returned by the backend, fixed 1–30 Hz axes, no smoothing, hidden point symbols and 4/8/13 Hz boundary markers.
- Tooltip displays the backend PSD value in `µV²/Hz`; no frontend filtering, FFT, integration or resampling was added.
- Chart instances are reused and resized through `ResizeObserver`; data updates do not recreate the component or chart instance.
- Removed the old SVG PSD point-generation path; the chart now consumes the backend frequency bins directly.

## Verification

- Frontend tests passed.
- `vue-tsc --noEmit` passed.
