## MODIFIED Requirements

### Requirement: Stateful filter configuration does not mix histories

The desktop SHALL validate high-pass, low-pass, and notch settings before use.
It SHALL reject a low-pass or enabled notch at or above the actual Nyquist
frequency. During recording or pause, the user MAY replace the display filter
configuration without changing device capture or raw persistence. The desktop
SHALL keep the existing complete display window visible while it creates a
candidate replacement. That candidate SHALL be calculated from a bounded span
of contiguous raw pre-roll plus the complete current visible window; it SHALL
not cross a sample-counter gap or fabricate samples. The desktop SHALL replace
the filtered display history only when the candidate contains the full visible
window, and future returned batches SHALL continue from the replacement
filter's final state. When less history than the bounded pre-roll is available,
it SHALL use the available contiguous context without claiming full filter
stabilization.

#### Scenario: User attempts to change a filter during recording

- **WHEN** a recording user selects a valid new high-pass, low-pass, or notch
  value
- **THEN** raw acquisition and raw persistence SHALL continue unchanged
- **AND** the old complete display window SHALL remain visible until the
  replacement candidate is complete
- **AND** the desktop SHALL atomically replace the old display history with the
  complete new visible window
- **AND** returned batches from the old configuration SHALL not appear after
  the replacement

#### Scenario: Warm-up context reaches a gap

- **WHEN** the most recent raw display history is preceded by a sample-counter
  gap
- **THEN** replacement filter warm-up SHALL begin after that gap
- **AND** the desktop SHALL not interpolate, zero-fill, or include data before
  the gap
