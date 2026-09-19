## MODIFIED Requirements

### Requirement: Stateful filter configuration does not mix histories

The desktop SHALL validate high-pass, low-pass, and notch settings before use.
It SHALL reject a low-pass or enabled notch at or above the actual Nyquist
frequency. During recording or pause, the user MAY replace the display filter
configuration without changing device capture or raw persistence. The desktop
SHALL initialize the replacement filter from no more than two seconds of the
most recent contiguous raw display data, SHALL not cross a sample-counter gap,
and SHALL replace visible filtered history with values from the replacement
configuration. When less than two seconds are available, it SHALL use the
available contiguous context without fabricating samples.

#### Scenario: User attempts to change a filter during recording

- **WHEN** a recording user selects a valid new high-pass, low-pass, or notch
  value
- **THEN** raw acquisition and raw persistence SHALL continue unchanged
- **AND** the desktop SHALL request a replacement backend display filter with
  its bounded contiguous raw warm-up context
- **AND** returned display values from the old configuration SHALL not appear
  in the replacement display history

#### Scenario: Warm-up context reaches a gap

- **WHEN** the most recent raw display history is preceded by a sample-counter
  gap
- **THEN** replacement filter warm-up SHALL begin after that gap
- **AND** the desktop SHALL not interpolate, zero-fill, or include data before
  the gap
