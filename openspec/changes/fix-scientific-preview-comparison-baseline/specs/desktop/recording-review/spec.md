## ADDED Requirements

### Requirement: Review separates ordinary colored viewing from scientific comparison

Ordinary Review SHALL retain its existing colored channel traces and independent viewer filters. A ready scientific comparison SHALL hide those traces and present only a light-gray scientific-filter baseline and a dark-gray processed trace with an explicit legend. These states SHALL share recording coordinates and physical sensitivity without feeding scientific preview samples into ordinary viewing or algorithm execution.

#### Scenario: User enters and exits scientific comparison

- **WHEN** a complete validated scientific pair becomes ready
- **THEN** the chart SHALL switch both scientific traces together and hide the ordinary colored traces and their sample markers
- **AND** leaving comparison SHALL restore ordinary channel colors, filter settings, playback position and visible range without requesting a new scientific calculation

#### Scenario: All enhanced policies are disabled

- **WHEN** comparison is ready with all enhanced policies disabled
- **THEN** the scientific baseline and processed samples SHALL coincide, the coincident trace SHALL NOT be made thicker by double drawing, and the UI SHALL state that only the common scientific filter is active

#### Scenario: Processed output is excluded or interpolated

- **WHEN** bad-channel treatment changes an output's availability or construction
- **THEN** comparison SHALL retain the baseline's ordered rows, identify missing processed outputs or interpolated dependencies explicitly, and keep gray line roles unambiguous
- **AND** unrelated outputs SHALL NOT be shifted into the changed output's row

### Requirement: Scientific comparison owns its filter explanation and display density

Scientific comparison SHALL identify its common scientific filter parameters, zero-phase method and the meaning of both gray traces. Its current filter SHALL remain 1–50 Hz with 50 Hz notch and Q=30, independent of ordinary viewer selectors and algorithm configurations. Both lines SHALL use equal nominal stroke width, common physical scaling and a point budget derived from the actual plot width within the declared API bounds.

#### Scenario: Viewer filter settings change while comparison is enabled

- **WHEN** ordinary high-pass, low-pass or notch selectors change
- **THEN** those settings SHALL apply only to ordinary colored viewing, the current scientific pair and confirmed ICA SHALL remain valid, and the UI SHALL distinguish the ordinary settings from the scientific pair's filter

#### Scenario: Actual drawing area resizes

- **WHEN** plot width changes enough to change the bounded point budget
- **THEN** Review SHALL coalesce a new display-only pair request using the current viewport, reuse the scientific preparations, and reject completion from a superseded width or viewport

### Requirement: Scientific comparison publishes only a complete current pair

Review SHALL publish a scientific pair atomically only for the current recording, analysis montage, configuration, viewport and display budget. During loading, failure, playback or temporary display-only montage use it SHALL clear scientific traces and show ordinary colored viewing with an explicit comparison status. Confirmed ICA state SHALL NOT change merely because display mode or density changes.

#### Scenario: An older comparison finishes after navigation or disposal

- **WHEN** a recording switch, seek, configuration change, resize, reset, disable or disposal supersedes a request
- **THEN** that request SHALL NOT publish either scientific trace or restore an obsolete comparison state

#### Scenario: Comparison request fails or its response is incompatible

- **WHEN** either scientific side fails or the paired response fails validation
- **THEN** Review SHALL show neither scientific trace, restore ordinary colors and explicitly state that scientific comparison is unavailable
- **AND** ordinary samples SHALL NOT be dimmed or described as the scientific baseline

#### Scenario: Selected montage or displayed rows cannot be matched

- **WHEN** the selected montage has no backend snapshot, or any currently displayed channel is absent from the scientific baseline under full channel identity matching
- **THEN** Review SHALL reject the comparison, keep ordinary colored traces and sample markers available, and explain that comparison is unavailable rather than silently request Raw

#### Scenario: Excluded bipolar output depends on its negative input

- **WHEN** the backend lists a displayed bipolar output in processed excluded_output_channels
- **THEN** Review SHALL mark that exact output excluded and explain that the remaining light-gray trace is the baseline, including when only the negative electrode is excluded

#### Scenario: Playback or a temporary display montage suspends comparison

- **WHEN** playback starts or the user selects an online temporary display montage
- **THEN** static scientific comparison SHALL suspend and ordinary colored viewing SHALL remain available
- **AND** on pause or return to a compatible analysis montage, an enabled comparison SHALL reload the current pair without changing scientific configuration
