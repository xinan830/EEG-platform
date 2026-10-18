## ADDED Requirements

### Requirement: PSD band-share chart is an independent view component
The WPF PSD detail page MUST render band shares through a standalone chart
component and MUST consume the backend `band_share` array without calculating
PSD, band power, or normalization in the client.
The page MUST offer a chart selector that shows either the PSD spectrum or the
band-share chart in the same main plot area. Changing the chart MUST NOT rerun
the algorithm or reset the selected dynamic time position.

#### Scenario: Show the latest complete dynamic band-share row
- **WHEN** a dynamic PSD timeline reaches a complete window
- **THEN** the chart shows that window's five backend-returned shares
- **AND** changing the timeline updates the chart without changing the PSD
  spectrum component.

#### Scenario: Switch the PSD chart
- **WHEN** the user selects band share or power spectral density
- **THEN** the corresponding SciChart view is shown in the same plot area
- **AND** the selected channel, Run, and dynamic time position are unchanged.
