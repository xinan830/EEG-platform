## ADDED Requirements

### Requirement: Playback state notifications represent state transitions

Review SHALL notify playback-state properties only when the shared playback coordinator changes those values. Advancing the render clock SHALL continue to publish the current recording position and algorithm cursor without publishing unchanged playback state or rebuilding waveform trace data.

#### Scenario: Playback advances within the playing state

- **WHEN** a render tick advances the recording cursor while playback remains active
- **THEN** Review SHALL publish the new cursor position and algorithm cursor
- **AND** SHALL NOT notify `IsPlaying` or `IsCompleted` with unchanged values
- **AND** the waveform canvas SHALL retain its bound trace data.

#### Scenario: Playback state changes

- **WHEN** playback starts, pauses, or reaches the recording end
- **THEN** Review SHALL notify each playback-state property whose value changed
- **AND** starting playback SHALL restore the complete waveform frame once.
