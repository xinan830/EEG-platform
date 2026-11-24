## ADDED Requirements

### Requirement: WPF renders an official STFT time-frequency result
The WPF STFT detail view MUST render the backend-provided single-channel
`power_db` matrix with declared time, frequency, and dB units. It MUST preserve
unavailable cells and MUST NOT calculate STFT or quality decisions locally.

#### Scenario: Static STFT result
- **WHEN** a static STFT Run returns a valid structured preview
- **THEN** the detail view displays its time-frequency image and axes
- **AND** its displayed time coordinates follow the backend time axis

#### Scenario: Dynamic STFT timeline
- **WHEN** the timeline reaches a complete dynamic window
- **THEN** the detail view displays that window's returned time-frequency matrix
- **AND** the X coordinates use the window start plus backend inner-time centers
- **AND** seeking to another window updates the image without another Run

#### Scenario: Rejected or unavailable window
- **WHEN** the selected dynamic window is rejected or unavailable
- **THEN** no earlier window's heatmap is presented as its result
- **AND** the unavailable state and backend window reason remain visible
