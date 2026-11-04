## Context

See `proposal.md` for motivation. The current PSD manifest accepts one source channel, and its dynamic result is a window-by-frequency series. The current topomap API and WPF control represent RBP bands through `bands`, `power_type`, and `band_id`. Review already owns the playback cursor, shared analysis settings, and current result-window selection; this change must extend those owners without introducing a second clock or a second PSD calculation.

The PSD scientific formula and configured preprocessing remain frozen. Backend calculation uses V and reports `V^2/Hz`; the map is a spatial projection of returned PSD values, not a new spectral estimator. Electrode positions continue to come from the applied montage snapshot and the shared topomap geometry.

## Goals / Non-Goals

**Goals:**
- Produce one aligned PSD spectrum for each configured channel using one shared frequency axis and identical scientific settings.
- Let Review select one frequency bin already present in that result and show the per-channel PSD field for the active result window.
- Use one scalar-field transport and one spatial projection/rendering path for PSD and RBP.
- Replace the current topomap response contract in one coordinated client/server change, with no legacy response reader.
- Use the shared boundary-interpolated trapezoidal `band_power` primitive for PSD band shares and Brainbeat dynamic bands without changing their band definitions, denominator, Welch window, or preprocessing.

**Non-Goals:**
- Change Welch segment length, filtering, analysis-window length, preprocessing, or quality-gate rules.
- Add a PSD-topomap algorithm ID, recompute PSD when the display frequency changes, or calculate EEG values in WPF.
- Add MNE as a runtime dependency or replace the existing spatial interpolation with `mne.viz.plot_topomap`.
- Convert historical single-channel PSD results into multi-channel results.

## Decisions

### 1. Make the official PSD result channel-resolved

The PSD input config will contain a nonempty ordered channel set. Review will capture the ordered analysis channels when the user applies the current PSD configuration. When an analysis montage is selected, that immutable montage output order supplies the set and its identity; otherwise the registered recording channel order can still produce spectra, but a topomap remains unavailable unless the authoritative position snapshot resolves those channels. Display-only montage state will not be used as a substitute for analysis inputs.

For a static result, `psd` has shape `[channel, frequency]` and `band_share` has shape `[channel, band]`. For a dynamic aggregate, they have shapes `[window, channel, frequency]` and `[window, channel, band]`. One-window dynamic artifacts use `[channel, frequency]` and `[channel, band]`. `channel_order` is the exact configured order; `frequency_hz` is a shared one-dimensional, strictly increasing axis. Per-channel status and failure reason align with the same channel dimension, including one status per channel in every dynamic window.

Each channel uses the existing four-second Welch segment, filters, preprocessing policy, window policy, and quality rules. A channel-local rejection does not invalidate neighboring channels. A mismatched frequency axis is a structured execution failure because matrix positions would otherwise be ambiguous.

PSD band shares and Brainbeat band power use the shared exact-boundary interpolation and trapezoidal integration. Brainbeat retains its IAPF-derived Theta/Alpha bands and 1–30 Hz denominator. Its Definition metadata must report the shared four-second Welch segment actually used by execution; this corrects stale metadata and does not alter the analyzed samples.

**Alternative considered:** run one single-channel PSD Run per electrode. This would repeat scheduling, provenance, result-window selection, and storage metadata, and could combine different windows or settings. One ordered channel-resolved Run gives the map one scientific identity and exact shared axis.

### 2. Select an exact frequency bin from the saved result

The Review selector is populated from the current result's `frequency_hz` axis. Its selection is a presentation field ID tied to that result, such as `psd.frequency-bin.<index>`, with the corresponding exact `frequency_hz` returned in field metadata. The backend validates the index against the bound result and reads that column from the channel matrix. It does not interpolate along the frequency axis, integrate adjacent bins, or create a new Run.

Topomap spatial interpolation remains the existing bounded interpolation over eligible channel coordinates. Only channels with values that pass their own quality state and map to valid scalp positions from the bound montage snapshot enter spatial interpolation. Other channel results remain valid PSD outputs but are reported as excluded from that map with reasons.

**Alternative considered:** accept arbitrary frequency values and interpolate neighboring PSD bins. Rejected because the user selected a single-frequency-bin PSD definition and interpolation would create a value not present in the official algorithm result.

### 3. Replace band payloads with explicit scalar spatial fields

The API request will identify one or more fields explicitly. PSD requests one exact frequency-bin field; RBP requests explicit relative/absolute band field IDs. Responses use `fields[]`, where every field carries its ID, label, unit, optional frequency metadata, channel points and states, grid, and provenance. `bands`, `power_type`, and `band_id` are removed from the current transport. All in-tree consumers move together; there is no fallback reader or default mapping from old identifiers.

The RBP adapter will read only its structured relative or absolute arrays and preserve existing units (`ratio` and `uV^2`). The PSD adapter will read only the current PSD Run arrays and preserve `V^2/Hz`. Neither adapter owns drawing code, raw recording reads, nor a calculation formula.

**Alternative considered:** add PSD data to the existing `bands` list. Rejected because a PSD frequency is not a physiological band and would make the response field name misstate the data. A new `psd-topomap` algorithm endpoint was also rejected because the map is a projection, not a second algorithm.

### 4. Keep Review as the sole time and result-window owner

The PSD line chart and topomap use the same PSD Run and the same current eligible window chosen by Review's time coordinator. Selecting a display channel or frequency bin changes presentation state only. A filter, analysis-window, or applied channel-set change creates a new configuration revision while preserving review position and playback state. Requests and visual preparation are keyed by Run, window, field, montage, canvas size, and generation so an old response cannot overwrite a newer selection.

The current channel quality state is displayed per channel. A bad electrode does not suppress other electrodes; a map with too few usable spatial points is explicitly unavailable. No zero fill, previous-window fill, guessed coordinate, or nearest-frequency fallback is allowed.

### 5. Cut over without a compatibility path

The current PSD result schema, topomap API models/routes, WPF DTOs, RBP adapter, PSD adapter, and all tests change in the same implementation. Request fingerprints or current-contract identity must prevent a historical single-channel Run from being reused as a channel-resolved Run. Historical records remain immutable and are not migrated; the current Review panel requests a new current-contract Run and reports incompatible old results as requiring recomputation.

No database row migration is planned unless inspection during implementation shows the persisted result container cannot store the new declared array dimensions. This is a schema and consumer cutover, not a rewrite of stored historical evidence.

## Risks / Trade-offs

- [PSD storage and compute grow with channel count] -> Process all requested channels in one windowed execution, reuse existing shared window/source preparation, avoid calculating maps for every frequency, and measure representative 19/32-channel playback and long recordings before acceptance.
- [One noisy channel could otherwise hide good channels] -> Store quality per channel/window and exclude only that channel from the spatial field.
- [A frequency bin may be mislabeled after a configuration change] -> Bind field IDs to result identity, derive labels from the returned axis, and reject requests whose result/window identity is stale.
- [Old PSD Runs have a different shape] -> Include the current output contract in Run compatibility identity and require a fresh Run; do not add an old-result adapter.
- [Changing RBP transport affects existing views] -> Enumerate every API, WPF, and test consumer and migrate them in the same change before removing old DTOs.
- [A numeric PSD field can have a wide dynamic range] -> Keep returned values and units exact; treat color mapping as presentation only and reuse the shared topomap renderer rather than changing science to improve contrast.

## Migration Plan

1. Implement the multi-channel PSD output and channel-local statuses under one new current result contract; keep scientific formulas and preprocessing unchanged.
2. Add the selected scalar field API and PSD/RBP adapters, then migrate every desktop and backend consumer to `fields[]` and explicit field IDs.
3. Make the Review PSD channel/frequency selectors read from the current channel-resolved result and submit no additional PSD Run when only presentation selection changes.
4. Deploy the backend and desktop together. Validate new results, RBP relative/absolute maps, PSD exact-bin maps, and old-run non-reuse.
5. If rollback is required, roll back the desktop and backend as a pair. Preserve newly written immutable Runs; the previous app may reject their output contract, and no compatibility conversion will be added during rollback.

## Open Questions

None. The requested PSD value definition, unit, selection behavior, and no-compatibility boundary are resolved in the proposal and specifications.
