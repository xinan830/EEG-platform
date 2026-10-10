## ADDED Requirements

### Requirement: Shared recording time display
WPF review and algorithm details SHALL share a session-level choice between
recording-relative seconds and acquisition clock display, defaulting to
recording-relative seconds. Internal sample coordinates and request seconds
SHALL remain unchanged.

#### Scenario: Switch during playback
- **WHEN** the user changes time display while viewing a recording
- **THEN** time labels, cursor and displayed ranges change representation
- **AND** playback state, position, selected channels, Run and data remain unchanged
- **AND** no analysis or data-load request is submitted by that change

#### Scenario: Display clock time
- **WHEN** a reliable recording start UTC is present
- **THEN** clock time is derived from that origin and recording-relative seconds
- **AND** clock axis labels include milliseconds using `HH:mm:ss.fff`
- **AND** a date is shown after a local date rollover
- **AND** the playback cursor provides both relative and clock time

#### Scenario: Fractional start preserves grid coordinates
- **GIVEN** a recording starts at local time 10:47:21.014
- **WHEN** review switches between recording and acquisition time
- **THEN** the grid remains aligned to recording seconds in both modes
- **AND** the 0-second and 1-second ticks display 10:47:21.014 and 10:47:22.014 in clock mode
- **AND** visible range, waveform and event coordinates remain unchanged

#### Scenario: Missing start metadata
- **WHEN** a recording has no reliable start UTC
- **THEN** clock selection is disabled and relative time remains available
- **AND** creation time and localized display text are not used as substitutes

### Requirement: Display is separate from analysis input
Analysis range inputs SHALL remain recording-relative seconds with explicit
unit labels. Only time axes SHALL use the shared time presentation.

#### Scenario: Change chart type
- **WHEN** a mixed chart switches from time trend to frequency or band view
- **THEN** frequency or category formatting is restored
- **AND** signal values, units, sample positions and gaps are unchanged
