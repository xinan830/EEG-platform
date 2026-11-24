## ADDED Requirements

### Requirement: Review can display adaptive markers for real projected samples

The desktop SHALL keep the continuous review waveform as the default display and SHALL provide a user-controlled point-and-line mode. When enabled, the desktop SHALL draw markers only at actual samples in the current bounded, montage-projected review frame, and SHALL hide the markers when the horizontal distance between adjacent samples is below 5 device-independent pixels (DIP). Hiding markers SHALL NOT remove or alter the continuous waveform. Markers SHALL preserve the frame's recording-time coordinates, output-channel order, and V/float64 values mapped through the existing display scale. Missing samples and recorded gaps SHALL remain unmarked and SHALL NOT be bridged.

The review workspace SHALL provide subsecond timebase choices of 0.1, 0.25, and 0.5 seconds per screen, in addition to its existing wider timebases, so users can zoom in far enough to inspect individual samples. Timebase selection SHALL affect only the visible review window.

#### Scenario: Point-and-line mode is off by default
- **WHEN** a recording is opened in the review workspace
- **THEN** the review waveform SHALL show continuous lines without sample markers

#### Scenario: User zooms in to reveal sample markers
- **WHEN** the user selects a subsecond timebase and enables point-and-line mode
- **THEN** the review viewport SHALL narrow to the selected timebase and display markers whenever actual samples are at least 5 DIP apart

#### Scenario: Markers appear when samples are visually separated
- **WHEN** the user enables point-and-line mode and adjacent samples are at least 5 DIP apart horizontally
- **THEN** the waveform SHALL show a marker at each visible real sample while retaining the continuous line

#### Scenario: Markers adaptively disappear when zoomed out
- **WHEN** point-and-line mode is enabled and adjacent samples are less than 5 DIP apart
- **THEN** the waveform SHALL hide sample markers and SHALL continue to show the continuous line

#### Scenario: Display-level extrema are used for the line
- **WHEN** the waveform line is rendered from an extrema-reduced display level
- **THEN** reduced extrema SHALL NOT be presented as sample markers

#### Scenario: A gap is present
- **WHEN** the projected review frame contains a non-finite sample or a sample-counter gap
- **THEN** no marker SHALL be drawn at the missing sample and the line SHALL remain discontinuous across the gap
