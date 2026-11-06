## Why

Review users need to compare PSD density and RBP power at one exact frequency bin or over a user-selected frequency range, for every channel in the same playback window. The existing review value panel only exposes five canonical bands, which cannot inspect narrower or single-bin behavior.

## What Changes

- Add a separate `自定义频段` entry and a two-pane review panel with an interactive PSD curve and a sortable, scrollable all-channel value table; preserve the existing five-band panel unchanged.
- Provide single-frequency and custom-range modes. The single-frequency cursor and both range boundaries snap to the nearest frequency in the current PSD grid and display the actual selected frequencies.
- Source PSD density from the current PSD result. Source absolute and relative power from the current RBP path for the same selected bin/range; derive RMS amplitude from the matching RBP absolute power in the backend.
- Define custom relative power as selected-range absolute power divided by total 1–50 Hz RBP power. For one point, use that frequency bin's actual interval.
- Reuse the current playback analysis window, preprocessing, channels and segmentation. Cursor/range interaction projects saved-window results and must not rerun Welch for each movement or block waveform seek/ticks.
- Preserve quality, units, window identity and provenance. Display unavailable values with their reasons and never substitute zeros.
- Keep saved RBP spectral sources backend-only: ordinary windows, scalar values and topomaps do not decode or transmit them. Reuse bounded validated decoded artifacts for custom projections and batch bin integration without changing scientific formulas.
- Own numeric-panel playback updates as matched PSD/RBP transactions: finish the active pair/projection and retain only the newest pending playback target. Accept the backend's actual step-aligned window at a fractional cursor; keep the last published pair with its explicit time while its successor loads. Seek/configuration/close invalidate immediately.

Compatibility: this is additive; the existing five-band numeric panel and topomap behavior remain available. The backend result projection contract must explicitly identify custom bounds, actual PSD bin frequency/width, RBP denominator, units and source window identity. No stored recording migration is intended. No new official algorithm is intended; custom interval support extends the existing PSD/RBP review projections. Scientific behavior changes only in the new panel's selected interval projection, with integration performed by the backend on the returned frequency grid.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/review-spectral-values`: add single-bin and arbitrary-range review while retaining existing canonical-band behavior.
- `analysis/official-rbp-faa-brainbeat-execution`: retain the already computed RBP spectrum in current on-demand windows and identify the expanded projection contract.

## Impact

The recording-review WPF toolbar, PSD curve/table presentation, existing PSD and RBP window adapters, and backend saved-window projection/API contracts are affected. The backend remains the sole owner of PSD lookup, custom-range integration, RBP normalization and RMS projection. No third-party dependencies or database migrations are planned. Validation must cover frequency-grid/bin boundaries, range integration, 1–50 Hz normalization, units, quality, source-window matching, interaction races, and playback independence.

Implementation scope updated on 2026-11-06: after business delivery, the user requested the initial XAML page as well. The implementation now includes the toolbar/menu entry, two-pane panel, numeric table and persistent SciChart curve/cursors. Real-recording acceptance remains an explicit open task; the user can refine the initial visual design.
