## MODIFIED Requirements

### Requirement: Use absolute recording time

Playback position, seek input, event times, window start, and returned elapsed samples SHALL use seconds derived from the session's authoritative recording-relative cursor and SHALL represent time from the start of the recording.

#### Scenario: Seek to a recording position

- **WHEN** the user seeks to `125.0 s`
- **THEN** the session coordinator converts that position using the declared sampling rate and cursor rounding rule
- **AND** waveform and analysis subscribers receive the same corresponding source sample boundary
- **AND** playback does not add the current view start a second time.

#### Scenario: Algorithm window request is delayed

- **WHEN** a scientific window request is queued or computing
- **THEN** waveform playback continues from the same coordinator cursor
- **AND** the request carries the cursor identity and does not create a competing display clock.
