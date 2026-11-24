## ADDED Requirements

### Requirement: Run PSD in Review through the shared on-demand channel

The recording-review workspace SHALL expose PSD only when the current PSD catalog entry is runnable and declares on-demand dynamic-window support. PSD SHALL use Review's registered recording, single recording-relative playback cursor, shared analysis binding and backend window coordinator. It SHALL NOT instantiate a retired PSD-specific Review provider, read saved PSD windows through a legacy route, create a full Run per cursor update, or calculate PSD in WPF.

PSD Review SHALL obtain its scientific version, default filter parameters and dynamic window/step values from the current algorithm catalog and configuration contract. It SHALL NOT duplicate those defaults independently in the desktop.

#### Scenario: Open PSD at the current Review cursor

- **WHEN** the user opens PSD from the recording-review workspace
- **THEN** Review sends a current-version dynamic PSD configuration for the selected available EEG source channel and current recording/montage identity
- **AND** the request uses the existing shared recording registration and Review cursor
- **AND** the chart displays only a backend-returned window whose actual range and identity passed the shared validation path.

#### Scenario: PSD catalog capability is unavailable

- **WHEN** PSD is missing, not runnable, or does not declare on-demand windows
- **THEN** PSD preparation reports an explicit unavailable/failure state
- **AND** waveform playback and the other registered algorithms remain usable
- **AND** no legacy PSD route is attempted.

#### Scenario: Render a ready PSD window

- **WHEN** the backend returns a ready PSD window matching the active recording, configuration and target
- **THEN** the PSD-owned presenter validates the `frequency_hz` axis in `Hz`, the `psd` array in `V^2/Hz` or `uV^2/Hz`, and their matching finite shape
- **AND** the Review panel renders the returned spectrum using the PSD module's display projection and chart
- **AND** the tooltip formats frequency and power values as decimals and presents the display unit as `µV²/Hz`
- **AND** when the pointer is within the rendered spectrum's hit tolerance, the hover marker snaps to the nearest returned PSD sample and the tooltip shows that sample's frequency and value
- **AND** when the pointer is outside the rendered spectrum's hit tolerance, the marker and tooltip are hidden
- **AND** the generic Review coordinator remains free of PSD arrays and formulas.

#### Scenario: PSD window is unavailable or rejected

- **WHEN** the backend returns insufficient, rejected, unavailable, cancelled or failed PSD output
- **THEN** the PSD panel shows the backend state/reason and actual range when present
- **AND** it clears the prior spectrum rather than showing stale or fabricated values.

#### Scenario: Change PSD source channel or close its panel

- **WHEN** the user changes the PSD source channel or closes/replaces the panel
- **THEN** the shared Review lifecycle invalidates or releases the matching foreground request/subscription
- **AND** channel changes preserve the Review cursor and playback state
- **AND** late responses from the prior configuration cannot replace the current PSD result.
