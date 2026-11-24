## MODIFIED Requirements

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
