# Band power trend primary view

- Added a backend-computed `band_power_timeseries` for Delta, Theta, Alpha and Beta on every Spectrogram time center.
- Added `BandPowerTrendChart.vue` as the primary explanatory chart above the detailed Heatmap.
- The frontend renders backend-integrated `µV²` values and does not integrate frequency bins itself.
- Bad quality windows remain gaps in the trend instead of connecting across rejected data.
- Trend x-axis is bounded to the actual returned time centers, so a rolling 18–28 s window is not displayed inside an unrelated 0–30 s axis.
- The trend remains an unsmoothed line: each point is a 4-second spectral window at a 1-second step, so visible angular changes are data changes rather than interpolation artifacts.
- The absolute-power Heatmap remains available as a detailed frequency view.

## Rationale

The trend chart answers “which band changed, and when?” more directly than a raw absolute-power heatmap. No automatic physiological or clinical conclusion is generated because no baseline or statistical change threshold has been defined.

## Verification

- Backend: 94 tests passed.
- Frontend: 35 tests passed.
- `vue-tsc --noEmit` passed.
