## MODIFIED Requirements

### Requirement: Live display-filter changes take effect at a sample boundary

The desktop SHALL validate high-pass, low-pass, and notch settings before use.
It SHALL reject a low-pass or enabled notch at or above the actual Nyquist
frequency. During recording or pause, the user MAY replace the display filter
configuration without changing device capture or raw persistence. The desktop
SHALL record the first raw sample counter after the user's action as the
effective boundary. Returned display values strictly before that boundary SHALL
remain from the previous display-filter session; values at and after that
boundary SHALL come from the replacement session. The desktop SHALL retain old
display history and SHALL NOT recalculate, clear, replace, or mutate it due to
the change.

The replacement session MAY receive bounded contiguous raw pre-roll solely to
initialize causal state. That pre-roll output SHALL NOT be displayed and SHALL
NOT rewrite display history. The renderer SHALL make the boundary a new trace
segment without creating a time gap.

#### Scenario: User attempts to change a filter during recording

- **WHEN** a recording user selects a valid new high-pass, low-pass, or notch
  value
- **THEN** raw acquisition and persistence SHALL continue unchanged
- **AND** display values before the recorded boundary SHALL remain unchanged
- **AND** the replacement session SHALL start at that boundary without
  displaying its pre-roll output
- **AND** the renderer SHALL not join previous and replacement traces with a
  line

#### Scenario: Warm-up context reaches a gap

- **WHEN** the most recent raw display history is preceded by a sample-counter
  gap
- **THEN** replacement filter pre-roll SHALL begin after that gap
- **AND** the desktop SHALL not interpolate, zero-fill, or include data before
  the gap

#### Scenario: A pending change is superseded

- **WHEN** a user selects another valid filter configuration before a pending
  change reaches its effective boundary
- **THEN** only the newest pending configuration SHALL take effect
- **AND** the renderer SHALL not retain a boundary for the superseded change
