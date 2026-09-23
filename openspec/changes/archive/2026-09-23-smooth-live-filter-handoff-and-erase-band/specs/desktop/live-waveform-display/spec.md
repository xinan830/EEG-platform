## ADDED Requirements

### Requirement: Live sweep uses a 0.3-second blank erase range

The live waveform SHALL distinguish newly written samples from retained
previous-page traces with a white erase range representing exactly 0.3 seconds
of the current x-axis time scale. The erase range SHALL begin at the current
received-sample write position and wrap across the page boundary when needed.
The renderer SHALL NOT draw a blue or other colored cursor line over the erase
range. The range is presentation-only and SHALL NOT remove raw, filtered, or
persisted samples.

When new received-sample positions arrive less frequently than the rendering
cadence, the renderer MAY interpolate the erase range only between the prior
received position and the newly received position. It SHALL NOT advance the
erase range past the latest received sample-counter position, and it SHALL snap
at a new page boundary rather than animate backward across the page.

#### Scenario: Sweep approaches the page boundary

- **WHEN** less than 0.3 seconds remains between the current write position and
  the right edge
- **THEN** the white range SHALL cover that remainder and continue from the left
  edge for the balance of 0.3 seconds
- **AND** no colored cursor line SHALL be visible
