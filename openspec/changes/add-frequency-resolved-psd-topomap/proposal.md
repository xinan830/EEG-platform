## Why

The current PSD result is a single-channel frequency series, while the current topomap contract represents RBP frequency bands. Review therefore cannot show the actual PSD density at a selected frequency across scalp electrodes. This change makes that view use the PSD algorithm's own per-channel frequency results and the recording's authoritative montage.

## What Changes

- **BREAKING** Extend the official PSD result contract to preserve an explicit ordered EEG channel set on one shared Welch frequency axis, including channel-local unavailable/quality states. Static spectra use channel x frequency arrays; dynamic spectra use window x channel x frequency arrays.
- Replace the band-only topomap response with a scalar spatial-field response. A PSD field selects one exact frequency bin from the current PSD result and keeps the PSD unit `V^2/Hz`; it does not recompute the spectrum or interpolate across frequency bins.
- Add a frequency selector and PSD topomap to the Review PSD panel, driven by bins present in the backend result and the Review workspace's current time window.
- Adapt current RBP relative/absolute fields to the shared spatial-field contract; update all in-tree consumers together and remove the band-only request/response path without a legacy response fallback.
- Keep topomap as a projection of an algorithm result, not a separately registered algorithm.
- Standardize PSD band shares and Brainbeat band powers on the shared boundary-interpolated trapezoidal integral while retaining each algorithm's frequency bands, denominator, and analysis windows.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `analysis/official-algorithm-catalog`: PSD accepts an ordered channel set and returns per-channel spectra on the shared frequency axis.
- `analysis/eeg-topomap`: spatial fields support exact-frequency PSD values as well as existing RBP fields through algorithm-owned adapters.
- `desktop/review-algorithm-link`: the PSD Review panel selects an actual returned frequency bin and renders its current-window spatial field.

## Impact

Affected areas include the PSD and Brainbeat scientific identities and band integration, PSD backend manifest/config/runner and result validation; the typed topomap API model, service and routes; the WPF topomap contracts/control and PSD Review adapter/view model; and backend, desktop, API and scientific-reference tests. Welch segment length, filtering, analysis windows and preprocessing remain unchanged. The PSD output shape and topomap response are breaking current contracts, so all current consumers must move together. Existing single-channel PSD runs are not migrated or converted; they cannot produce a topomap, and the current panel must request a new run under the current contract. No PSD calculation is inferred from RBP values, no frontend EEG calculation is added, and no new runtime dependency is required.
