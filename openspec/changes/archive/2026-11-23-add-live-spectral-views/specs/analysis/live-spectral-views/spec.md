## ADDED Requirements

### Requirement: Acquisition exposes shared spectral computations on a rolling window

The acquisition workspace SHALL expose PSD, STFT, RBP topography and PSD topography using the review science primitives and chart controls. Windows SHALL use session-relative time and the live coordinator.

#### Scenario: A view starts with retained data

- **GIVEN** a continuous ten-second interval is retained
- **WHEN** an operator starts a spectral view
- **THEN** the view receives the newest retained window and updates at one-second sample-aligned intervals

#### Scenario: A live chart starts at activation time

- **GIVEN** acquisition has reached 23 seconds and the operator opens RBP or STFT
- **THEN** the chart's visible time range begins at the algorithm activation time, without empty history from session start
- **AND** the first STFT heatmap cell begins at the activation time even when its time-window center is later
- **AND** an immediately available rolling result may use samples collected before activation
- **AND** only RBP points and STFT columns at or after activation are rendered
- **AND** calculation windows, sample coordinates, and session-relative result timestamps remain unchanged

#### Scenario: Spatial mapping is insufficient

- **WHEN** a topography window lacks enough valid electrode positions or values
- **THEN** the panel reports that the map is unavailable and does not reuse a previous map
