# Recording Lifecycle Boundaries

## ADDED Requirements

### Requirement: Persist lifecycle boundaries
The desktop MUST persist started, paused, resumed, and stopped boundaries with the last or first persisted sample counter applicable to that transition and the UTC operation timestamp.

#### Scenario: Pause does not lead persisted data
- **WHEN** pause is requested while a batch is waiting to be written
- **THEN** the pause boundary uses the last sample counter already persisted, and the skipped interval remains an explicit gap

### Requirement: Display lifecycle boundaries
Acquisition and review waveforms MUST display lifecycle boundaries as dashed vertical markers with millisecond wall-clock labels derived from the recording start and sample coordinate.

#### Scenario: Review shows a pause gap
- **WHEN** a recording contains a pause and resume boundary
- **THEN** review shows the boundaries and preserves the interval without fabricated waveform samples

### Requirement: Light review grid
Review major grid lines MUST use a light gray stroke suitable for waveform inspection.

#### Scenario: Review grid is not black
- **WHEN** a review waveform is rendered
- **THEN** major vertical grid lines use the configured light gray stroke
