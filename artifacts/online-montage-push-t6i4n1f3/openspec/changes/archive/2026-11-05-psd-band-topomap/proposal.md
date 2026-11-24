## Why

PSD Review previously exposed integrated band power in `µV²` while describing it as PSD. Integrated power and power spectral density are different quantities. The five-band selector must retain the density meaning and produce a value in `µV²/Hz` for each canonical frequency range.

## What Changes

- Replace PSD single-frequency-bin Review selection with Delta, Theta, Alpha, Beta, and Gamma band-mean-density selection.
- Compute each selected value from the current channel's PSD density as the trapezoidal integral over the canonical band divided by the complete band width.
- Keep the backend value in `V^2/Hz`; convert to `µV²/Hz` only for WPF scale labels. Keep the PSD source and RBP result path independent.
- Render the five named options and bind selection to explicit PSD band field IDs.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `analysis/eeg-topomap`: PSD topomap supports explicit band-mean-density fields alongside exact frequency-bin fields.
- `desktop/review-algorithm-link`: PSD Review selects one of the five canonical band-mean-density fields.

## Impact

- Backend: shared frequency-band contract, PSD and RBP band declarations, PSD topomap adapter, and tests.
- WPF: PSD review selector, request field ID, field presentation, and tests.
- No database migration or dependency change. Historical PSD/RBP results remain unchanged; the new PSD band fields apply to current PSD windows only.
- The PSD Review selector changes from one frequency bin to band-mean PSD (`V^2/Hz` in the API; `µV²/Hz` in the display). It does not change exact-frequency API fields, PSD spectra, PSD band-share output, RBP results, or algorithm runs.
