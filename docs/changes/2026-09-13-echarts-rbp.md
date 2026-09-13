# ECharts RBP chart migration

- Added `BandPowerChart.vue` using ECharts 6 with a horizontal 100% stacked bar chart.
- The chart consumes backend RBP values directly; it does not normalize or recalculate them.
- Added responsive resize handling and a percentage tooltip/legend.
- Removed the unused Chart.js dependency after migrating the only Chart.js consumer.
- PSD and Spectrogram remain on their existing renderers until their dedicated ECharts migrations are completed.

## Verification

- Frontend tests passed.
- `vue-tsc --noEmit` passed.
