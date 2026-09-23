# desktop/live-waveform-display Specification

## Purpose
TBD - created by archiving change smooth-live-waveform-rendering. Update Purpose after archive.
## Requirements
### Requirement: Bounded live waveform rendering

The desktop client SHALL render live EEG with work bounded by the visible
screen density rather than by vendor batch count or total retained history.
Frame construction SHALL not block the WPF dispatcher, and pending stale
frames SHALL not form an unbounded rendering backlog. For each contiguous
screen-density bucket, the renderer SHALL emit no more than four actual
chronological samples: first, minimum, maximum, and last. Duplicate sample
roles SHALL be emitted once.

#### Scenario: Vendor emits small batches

- **WHEN** many small consecutive batches cover one display window
- **THEN** the renderer SHALL aggregate them into chronological extrema at a
  bounded multiplier of visible bucket count
- **AND** batch boundaries SHALL not increase rendered point count beyond the
  per-bucket temporal-extrema bound

#### Scenario: A bucket contains a peak and trough

- **WHEN** one screen-density bucket contains distinct minimum and maximum
  samples
- **THEN** both SHALL be rendered at their actual chronological sample positions
- **AND** the renderer SHALL NOT place them at the same horizontal coordinate
  or interpolate an average value

### Requirement: Received-sample time authority

The sweep cursor and time labels SHALL be derived from the latest completed
frame built from received samples. Rendering optimization SHALL not advance
time beyond the latest received sample or fill a raw sample-counter gap.

#### Scenario: Raw sample counter has a gap

- **WHEN** consecutive retained batches have a discontinuous raw sample counter
- **THEN** the waveform SHALL start a new visible segment after the gap
- **AND** the renderer SHALL not interpolate samples across it

#### Scenario: Recording is paused

- **WHEN** raw recording is paused while the device stream remains open
- **THEN** the sweep cursor and waveform SHALL continue from newly received
  display samples
- **AND** the paused interval SHALL affect raw-recording audit data, not the
  visual sample-counter timeline

### Requirement: Live waveform paper speed is a physical-layout presentation setting

The desktop SHALL offer supported live waveform paper speeds of 5, 10, 15, 30,
and 60 mm/s. It SHALL derive the visible horizontal time duration from the
rendered canvas width and selected paper speed, rather than from a fixed
seconds-per-screen selection. The speed SHALL be a local display preference and
SHALL NOT alter acquisition sampling rate, sample counters, raw EEG values,
raw persistence, display filtering, or scientific analysis.

#### Scenario: User selects 30 mm/s

- **WHEN** the nominal rendered width is 30 mm and paper speed is 30 mm/s
- **THEN** the x-axis SHALL represent one second across that width
- **AND** changing the width or speed SHALL recompute visible seconds without
  inventing or advancing received samples

#### Scenario: User changes paper speed during acquisition

- **WHEN** the user changes paper speed while the waveform is rendering
- **THEN** the renderer SHALL rebuild only presentation paging and time-axis
  geometry from already received samples
- **AND** raw persistence and the acquisition read loop SHALL continue unchanged

#### Scenario: A monitor has uncertain physical calibration

- **WHEN** the workstation display cannot guarantee ruler-exact physical length
- **THEN** the desktop SHALL retain resolution-independent nominal layout speed
- **AND** it SHALL NOT represent the setting as calibrated clinical paper output

### Requirement: Live channel controls represent selected montage outputs

When a live acquisition uses a selected display montage, the live settings
popup SHALL show that montage's derived output count and names rather than a
static physical-input count or physical input-label checklist. Selecting an
output in the popup SHALL control only whether that derived output is rendered
in the active live session. It SHALL NOT alter the montage formula, montage
order, source channel configuration, raw acquisition values, raw persistence,
or scientific analysis reference.

#### Scenario: A bipolar montage has a subset of source channels

- **WHEN** the selected montage derives a subset of its source configuration,
  such as a bipolar chain
- **THEN** the popup SHALL report and list only that derived subset
- **AND** disabling one listed item SHALL remove only its live waveform trace

#### Scenario: No montage is selected

- **WHEN** the live settings popup is opened before a montage is selected
- **THEN** it SHALL report that no montage is selected
- **AND** it SHALL NOT fabricate a standard 10-20 channel count or reference mode

### Requirement: Live sweep uses a 0.3-second blank erase range

The live waveform SHALL distinguish newly written samples from retained
previous-page traces with a white erase range representing exactly 0.3 seconds
of the current x-axis time scale. The erase range SHALL begin at the current
received-sample write position and wrap across the page boundary when needed.
The renderer SHALL NOT draw a blue or other colored cursor line over the erase
range. The range is presentation-only and SHALL NOT remove raw, filtered, or
persisted samples.

#### Scenario: Sweep approaches the page boundary

- **WHEN** less than 0.3 seconds remains between the current write position and
  the right edge
- **THEN** the white range SHALL cover that remainder and continue from the left
  edge for the balance of 0.3 seconds
- **AND** no colored cursor line SHALL be visible
