## Purpose

为每个打开的录音会话提供一个可追溯、单一所有权的播放时间源，使波形显示、算法详情和动态窗口请求使用同一个录音相对游标，同时保持读取与科学计算异步独立。

## ADDED Requirements

### Requirement: Own one authoritative recording cursor

For each open recording session, the desktop SHALL have exactly one authoritative playback context. Its cursor SHALL be a non-negative recording-relative integer sample boundary derived from the recording sampling rate and duration; displayed seconds SHALL be derived from that cursor and SHALL use absolute recording time from the recording start.

#### Scenario: Playback advances a recording
- **WHEN** the session is playing
- **THEN** the context advances one cursor and all subscribers observe the same cursor sample and derived seconds
- **AND** no subscriber creates a second clock or independently estimates recording time.

#### Scenario: Cursor reaches a boundary
- **WHEN** playback reaches the recording duration
- **THEN** the context clamps the cursor to the valid final boundary and publishes completion once
- **AND** subscribers do not receive a cursor beyond the recording sample count.

### Requirement: Coordinate controls and context generations

The playback context SHALL own play, pause, seek, playback-rate changes and context reset. A seek, recording change, montage/source change, or explicit restart SHALL advance a generation so consumers can reject events from the prior context.

#### Scenario: Seek while a window request is pending
- **WHEN** the user seeks from target A to target B
- **THEN** the context publishes B with a newer generation
- **AND** a late result associated with A cannot replace state associated with B.

#### Scenario: Change recording
- **WHEN** the open recording changes
- **THEN** the old context is stopped and disposed before the new recording context is published
- **AND** no old recording cursor or result target is forwarded to the new session.

### Requirement: Keep subscribers asynchronous and independent

Cursor publication SHALL be non-blocking with respect to waveform reads, display rendering, backend algorithm requests and scientific computation. A slow subscriber SHALL not delay the context's clock or another subscriber; each subscriber SHALL coalesce obsolete work and retain explicit pending/lag state.

#### Scenario: Algorithm computation is slow
- **WHEN** a dynamic algorithm request remains queued or computing
- **THEN** the playback cursor and waveform path continue advancing
- **AND** the algorithm subscriber reports pending or lag state without blocking playback.

### Requirement: Expose traceable cursor events

Each published cursor event SHALL include recording identity, generation, cursor sample, sampling rate, derived absolute seconds, play state and playback rate. Events SHALL be ordered per context and SHALL not contain signal arrays or scientific result values.

#### Scenario: Subscriber receives a cursor event
- **WHEN** a waveform or algorithm subscriber handles an event
- **THEN** it can derive the same sample/time coordinate and context identity without reading a second clock
- **AND** an invalid identity or out-of-range sample is rejected explicitly.
