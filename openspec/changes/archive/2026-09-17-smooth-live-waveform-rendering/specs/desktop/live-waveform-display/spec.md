## ADDED Requirements

### Requirement: Bounded live waveform rendering

The desktop client SHALL render live EEG with work bounded by the visible
screen density rather than by vendor batch count or total retained history.
Frame construction SHALL not block the WPF dispatcher, and pending stale
frames SHALL not form an unbounded rendering backlog.

#### Scenario: Vendor emits small batches

- **WHEN** many small consecutive batches cover one display window
- **THEN** the renderer SHALL aggregate them into screen-density extrema
- **AND** batch boundaries SHALL not increase the rendered point count beyond the visible bucket bound

### Requirement: Received-sample time authority

The sweep cursor and time labels SHALL be derived from the latest completed
frame built from received samples. Rendering optimization SHALL not advance
time beyond the latest received sample or fill a raw sample-counter gap.

#### Scenario: Raw sample counter has a gap

- **WHEN** consecutive retained batches have a discontinuous raw sample counter
- **THEN** the waveform SHALL start a new visible segment after the gap
- **AND** the renderer SHALL not interpolate samples across it

#### Scenario: Acquisition is paused

- **WHEN** acquisition is paused and no new displayed samples arrive
- **THEN** the sweep cursor SHALL remain at its last completed position

