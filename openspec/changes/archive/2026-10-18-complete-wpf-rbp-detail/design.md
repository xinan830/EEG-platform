# Design

`RbpResultPreview` parses `result_summary.metric.band_values` for a static Run
and `result_summary.metric.series[*].band_values` for a dynamic Run. It keeps
the backend's Delta, Theta, Alpha, Beta, Gamma order for current v2 Runs,
preserves historical four-band Runs, and retains window boundaries, state,
quality, and failure reason. Missing, invalid, or quality-rejected values stay
null.

The existing catalog time cursor releases dynamic RBP windows by their backend
window end time. The RBP-specific view model projects the current window and
the trend up to that cursor. A rejected window leaves a chart gap rather than
repeating the previous measurement. The RBP-owned SciChart component renders
the backend-provided shares and trend lines without calculating scientific
values.

The shared quality card reads RBP state and backend failure evidence. Existing
PSD/STFT selection and result paths remain unchanged.
