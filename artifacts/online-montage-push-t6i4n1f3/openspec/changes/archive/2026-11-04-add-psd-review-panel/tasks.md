## 1. Contract and configuration

- [x] 1.1 Add the Review PSD contract with current-version, on-demand-only and no-legacy-route acceptance criteria.
- [x] 1.2 Expose current PSD filter defaults in the backend catalog and add PSD Review parameters from catalog values/policy and available recording/montage channels; test their alignment and exact serialized config.
- [x] 1.3 Remove the retired PSD band-share result fallback and verify current PSD presentation ignores retired `spectral_evidence.relative_band_power` data.

## 2. PSD-owned presentation

- [x] 2.1 Add a Review PSD presenter that reuses the official PSD window validator/projection and preserves unavailable/failure reasons without retaining stale spectra.
- [x] 2.2 Add focused tests for valid axes/units, malformed result rejection, and unavailable/rejected state clearing.

## 3. Shared Review composition

- [x] 3.1 Register the existing generic on-demand adapter for `psd` and wire the PSD-owned configuration/presenter into Review.
- [x] 3.2 Add PSD menu/toolbar entry and a Review panel that reuses the existing PSD spectrum chart.
- [x] 3.3 Test opening at the active cursor, channel-change revision, close/replacement cleanup, and absence of a second clock or full Run path.

## 4. Verification

- [x] 4.1 Run focused Review tests, desktop build, and relevant backend capability/config tests; run OpenSpec strict validation and `git diff --check`.
- [x] 4.2 Record manual WPF acceptance and any unverified environment-dependent behavior.
- [x] 4.3 Format PSD tooltip values as decimals and present the display unit as `µV²/Hz` without changing API units.
- [x] 4.4 Snap PSD hover to the nearest returned sample only within the rendered spectrum's hit tolerance; hide the marker and tooltip outside it.
