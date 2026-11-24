## ADDED Requirements

### Requirement: Retire the legacy metric playback transport

The backend SHALL expose waveform playback for reviewing source samples and
SHALL NOT expose the retired stateful metric playback session endpoints.
Waveform playback SHALL NOT substitute old metric-stream outputs, and official
scientific metrics SHALL continue to use versioned Analysis Runs.

#### Scenario: Request the retired metric playback API

- **WHEN** a client requests `/api/recordings/{recording_id}/playback` or
  `/api/playback/{session_id}/control`
- **THEN** no route is registered and the request receives HTTP 404
- **AND THEN** no legacy EEG processor or metric session is constructed

#### Scenario: Review a recording after retirement

- **WHEN** the client requests `/api/recordings/{recording_id}/waveform-playback`
- **THEN** waveform playback retains its existing sample, time, filter,
  montage, and WebSocket behavior
- **AND THEN** it does not emit the old metric-stream payloads
