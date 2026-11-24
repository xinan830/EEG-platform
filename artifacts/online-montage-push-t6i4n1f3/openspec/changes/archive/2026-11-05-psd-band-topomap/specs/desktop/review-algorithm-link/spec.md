## ADDED Requirements

### Requirement: Review maps one selected PSD band from the current result
PSD Review SHALL use one channel-resolved PSD result for its spectrum and topomap. Its topomap selector SHALL list exactly Delta (1–4 Hz), Theta (4–8 Hz), Alpha (8–13 Hz), Beta (13–30 Hz), and Gamma (30–50 Hz), and SHALL bind each option to the matching canonical PSD band-mean-density field. For each channel, the backend SHALL integrate the PSD density over the exact band edges and divide by the complete band width. It SHALL retain `V^2/Hz` in the API and report the exact frequency bounds. WPF SHALL convert that value once for display as `µV²/Hz`. The map SHALL use the same current PSD window and bound montage snapshot. Selection SHALL NOT start another PSD Run, reread raw recording samples, calculate values in WPF, or use RBP band-power arrays. Review's shared time coordinator SHALL select the current eligible PSD window and prevent an obsolete response from replacing a newer cursor/window identity.

#### Scenario: Select a canonical band for the current PSD window
- **WHEN** the user selects one of the five canonical PSD bands
- **THEN** the topomap displays the backend-computed per-channel band-mean PSD for exactly that band and current window
- **AND** the response reports the band's exact frequency bounds and `V^2/Hz`
- **AND** the color scale displays `µV²/Hz` after one conversion from `V^2/Hz`
- **AND** the PSD result identity and review playback state remain unchanged

#### Scenario: The selected band is unavailable in the current result
- **WHEN** the bound PSD frequency axis does not cover the selected band's complete range or channel values are unusable
- **THEN** the topomap reports a structured unavailable or invalid-field state
- **AND** it does not clip the range, select another band, or use a previous window

#### Scenario: Playback advances while a PSD topomap is loading
- **WHEN** Review advances to a newer eligible PSD window before the previous topomap response completes
- **THEN** the older response cannot replace the newer window's field
- **AND** the waveform remains controlled by the single Review time coordinator

## REMOVED Requirements

### Requirement: Review maps a selected PSD frequency from the current result
**Reason**: The PSD Review topomap selector now exposes five canonical band-mean-density fields. Exact frequency-bin fields remain available to backend API consumers but are no longer the Review selector contract.
**Migration**: Review clients select `psd.band-mean-density.<band>` and consume the returned `frequency_range_hz` and `V^2/Hz` unit. Backend consumers that need one exact frequency continue to request `psd.frequency-bin.<index>` and receive `frequency_hz` with `V^2/Hz`.
