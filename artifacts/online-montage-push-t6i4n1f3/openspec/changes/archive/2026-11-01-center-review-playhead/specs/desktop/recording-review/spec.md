## ADDED Requirements

### Requirement: Review follows the playhead at the viewport center
Review SHALL center the playback position within the visible recording window
whenever recording bounds permit. The viewport SHALL NOT extend outside the
recording. Navigation SHALL preserve sample time, gaps, filtering and analysis
configuration, with no independent playhead animation.

#### Scenario: Drag from start through middle to end
- **GIVEN** a 100-second recording with a 10-second visible window
- **WHEN** the bottom navigator selects 0, 2, 5, 50, 95, 98 and 100 seconds
- **THEN** viewport starts SHALL be 0, 0, 0, 45, 90, 90 and 90 seconds
- **AND** playhead fractions SHALL be 0, 0.2, 0.5, 0.5, 0.5, 0.8 and 1
- **AND** reverse navigation SHALL produce the same positions in reverse order

#### Scenario: Short recording
- **GIVEN** the recording is shorter than the configured visible duration
- **WHEN** the user navigates from its first to last recorded time
- **THEN** the viewport SHALL remain at the start and the playhead SHALL move from left to right

### Requirement: Navigator selects the full recording position
The bottom navigator SHALL bind the actual playback position. Thumb travel
SHALL map to the full 0..duration range, not viewport-start range. Existing
bounded drag scheduling and atomic track-click loading SHALL remain intact.

#### Scenario: Drag begins without an offset
- **WHEN** dragging begins at the current thumb position
- **THEN** rate limiting SHALL start at the current recording position
- **AND** the thumb mapping SHALL round-trip that position without a viewport offset

#### Scenario: Both endpoints are reachable
- **WHEN** the thumb reaches either end of its available travel
- **THEN** the selected time SHALL be exactly 0 or recording duration respectively
- **AND** this SHALL also hold for short recordings

### Requirement: Playhead is visually distinct and plot aligned
Review SHALL display the playhead as a dark-gray thin dashed line with a small
downward top marker inside the waveform plot. It SHALL NOT intercept waveform
input or overlap axis labels. Switching time-label modes SHALL preserve its
recording coordinate and plot alignment.

#### Scenario: Chart layout changes
- **WHEN** the chart resizes or its axis labels change
- **THEN** the playhead SHALL use the actual plot area rather than chart-host width
- **AND** its start, middle and end positions SHALL remain within that plot
