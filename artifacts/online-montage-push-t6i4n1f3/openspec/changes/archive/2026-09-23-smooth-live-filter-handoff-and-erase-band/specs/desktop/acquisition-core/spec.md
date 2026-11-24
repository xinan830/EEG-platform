## MODIFIED Requirements

### Requirement: Live display-filter changes take effect at a sample boundary

The desktop SHALL validate high-pass, low-pass, and notch settings before use.
It SHALL reject a low-pass or enabled notch at or above the actual Nyquist
frequency. During recording or pause, the user MAY request a replacement
display filter without changing device capture or raw persistence. The active
filter SHALL continue producing visible batches while the replacement session
is created, receives bounded contiguous binary float64/V pre-roll, and catches
up with post-request display blocks. Pre-roll and catch-up output SHALL NOT be
displayed.

After the replacement has processed through a completed active block, the
desktop SHALL atomically activate it for the next sample and record that sample
counter as the effective boundary. Display values before the effective boundary
SHALL remain from the previous session; values at and after it SHALL come from
the replacement session. Existing history SHALL NOT be recalculated, cleared,
replaced, crossfaded, or mutated. The renderer SHALL start a new trace segment
at the boundary without creating a time gap.

#### Scenario: User attempts to change a filter during recording

- **WHEN** a recording user selects a valid new high-pass, low-pass, or notch value
- **THEN** raw acquisition and persistence SHALL continue unchanged
- **AND** the currently active filter SHALL remain visible during preparation
- **AND** the replacement SHALL activate only after catching up at a sample boundary
- **AND** the renderer SHALL not join previous and replacement traces with a line

#### Scenario: Replacement filter preparation takes longer than one display block

- **WHEN** a valid replacement filter is still being created or warmed while
  new acquisition blocks arrive
- **THEN** the active filter SHALL continue to produce those visible blocks
- **AND** the received-sample timeline SHALL not pause or jump
- **AND** the replacement SHALL receive the contiguous blocks needed to catch up
- **AND** raw acquisition and persistence SHALL continue unchanged

#### Scenario: Replacement catches the active stream

- **WHEN** the replacement filter has processed through the latest completed
  active-filter block
- **THEN** the next raw sample SHALL become the recorded effective boundary
- **AND** only boundary-and-later display values SHALL use the replacement
- **AND** no synthetic crossfade values SHALL be produced

#### Scenario: Warm-up context reaches a gap

- **WHEN** the most recent raw display history is preceded by a sample-counter gap
- **THEN** replacement filter pre-roll SHALL begin after that gap
- **AND** the desktop SHALL not interpolate, zero-fill, or include data before the gap

#### Scenario: A pending change is superseded

- **WHEN** a user selects another valid filter configuration before a pending
  replacement activates
- **THEN** the superseded session SHALL not become active
- **AND** only the actual successful activation SHALL create a display boundary
- **AND** the current active filter SHALL continue to provide visible output
