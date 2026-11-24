## ADDED Requirements

### Requirement: Bounded FAA and IAPF playback preparation
Review SHALL prepare at most two upcoming step-aligned FAA/IAPF windows through existing backend adapters and adopt a prepared result only when its playback target is reached. Preparation SHALL NOT mutate the current window, chart, playback clock or scientific parameters. Actual absolute sample bounds, units, channel order, quality and provenance SHALL remain backend-owned and validated at adoption.

#### Scenario: Prepared scalar window reaches its target
- **WHEN** the next FAA/IAPF window finishes before the playback cursor reaches its configured step
- **THEN** the current result remains unchanged until that target and the prepared backend window is adopted without a second scientific request at the target

#### Scenario: Scalar preparation misses a deadline
- **WHEN** playback overtakes a pending scalar window
- **THEN** waveform playback proceeds independently, preparation remains bounded, only the newest pending target is retained, and old windows cannot be relabelled as current

#### Scenario: Scalar playback lifecycle changes
- **WHEN** playback pauses, seeks, drags, changes configuration, detaches or closes
- **THEN** pending preparation is cancelled and discarded and late responses cannot publish into the new lifecycle
