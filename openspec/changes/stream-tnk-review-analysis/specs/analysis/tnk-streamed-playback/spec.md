## Purpose

Define how the TNK brain-metric group publishes real, ordered analysis-window results during recording review while keeping waveform playback responsive and preserving scientific state, result identity, and explicit history coverage.

## ADDED Requirements

### Requirement: Keep TNK analysis independent from waveform playback
The desktop SHALL advance and render the review waveform without waiting synchronously for TNK queueing, source preparation, computation, result delivery, or chart projection. TNK work SHALL use bounded asynchronous scheduling and SHALL expose queue saturation or failure without blocking the playback clock.

#### Scenario: TNK computation is slower than playback
- **WHEN** a TNK window takes longer than one playback step to compute
- **THEN** the waveform continues advancing on its playback clock
- **AND** the TNK display identifies the newest completed analysis window and its lag from the cursor
- **AND** no old TNK value is labeled as if it were calculated at the current cursor

### Requirement: Publish TNK results as identified analysis-window events
Each TNK result event SHALL carry the immutable window identity, recording/session and scientific-configuration identity, algorithm and implementation identity, half-open sample range, recording-relative start/end time, output units, quality state, and structured failure/evidence when applicable. Consumers SHALL order and deduplicate by window identity and analysis time, not by response arrival time or current cursor position.

Ordered delivery SHALL be explicitly opted into on the existing on-demand ensure request. It SHALL use a separate playback delivery identity and a resumable event-read endpoint. Intermediate frames SHALL use a stable scientific identity key, explicitly distinguished from the persisted target `window_id` accepted by the existing window-read endpoint. Unsupported algorithms, delivery-session capacity, and asynchronous publication failure SHALL have structured outcomes. Requests without opt-in SHALL retain the existing response and execution behavior.

#### Scenario: A result arrives after the cursor advances
- **WHEN** a result for an earlier window arrives after the review cursor has moved forward
- **THEN** the result is plotted at its own analysis-window time
- **AND** its computation lag is reported separately from its analysis time

#### Scenario: Results arrive out of order or more than once
- **WHEN** the subscriber receives duplicated or out-of-order events for the active scientific session
- **THEN** it merges each window at most once in analysis-time order
- **AND** events from a different recording, session, configuration, or implementation are rejected from the active projection

### Requirement: Preserve TNK stateful window continuity
The TNK group SHALL execute every state transition required by the canonical dynamic frame grid in order, using a compatible checkpoint or replay from the declared reset point. Replacing an unstarted playback target SHALL NOT skip a state transition required to compute a later target. Window length, step, warm-up, preprocessing, quality, and formulas SHALL remain those of the resolved scientific configuration.

#### Scenario: Latest playback target skips unstarted display requests
- **WHEN** a newer target replaces one or more unstarted targets for a stateful TNK module
- **THEN** the backend restores a compatible checkpoint and replays all required canonical frames through the requested target
- **AND** each transition uses its actual source window and quality outcome

#### Scenario: Seek backward or change scientific context
- **WHEN** the user seeks backward, changes recording, channel-role mapping, filters, parameters, or algorithm version
- **THEN** results and checkpoints from an incompatible context are not reused
- **AND** analysis resumes from a compatible earlier checkpoint or the declared reset point

### Requirement: Account for every canonical frame in playback history
During uninterrupted TNK playback, every canonical frame that the backend computes while advancing state SHALL become available to the active history projection in analysis-time order. The system SHALL distinguish complete, partial, rejected, unavailable, failed, and not-yet-computed frames. If bounded delivery capacity is exceeded or a stream is interrupted, the client SHALL expose the missing range and provide a reconciliation path; it SHALL NOT silently claim complete history.

#### Scenario: Backend replays intermediate frames to a later target
- **WHEN** backend state replay computes intermediate canonical frames before the requested target
- **THEN** those frame results are made available to the active playback history, or an explicit coverage gap is reported
- **AND** each frame retains its own window bounds, quality, and failure semantics

#### Scenario: Event delivery overflows or disconnects
- **WHEN** a bounded subscriber buffer overflows or delivery is interrupted
- **THEN** the client receives an explicit discontinuity/coverage state with the affected range
- **AND** it can reconcile the range through the supported window/history read path without presenting the chart as complete

### Requirement: Append TNK timeline results without unbounded UI work
The desktop SHALL append newly accepted TNK timeline points when the existing prefix is unchanged. Per-event projection and chart work SHALL be bounded and SHALL NOT rebuild unrelated waveform data. A reset, incompatible identity, or explicit history replacement MAY rebuild the TNK projection.

#### Scenario: A new TNK frame is accepted
- **WHEN** a new valid or quality-rejected TNK frame arrives for the active session
- **THEN** the corresponding value or explicit gap/state marker is appended at that frame's analysis time
- **AND** the playback cursor and waveform series are not reset by the TNK chart update

### Requirement: Measure playback and TNK stages independently
The system SHALL expose bounded diagnostics for waveform tick interval/handler duration, queue wait/depth, source preparation, state replay/computation, event delivery, history projection, and chart update. Performance acceptance SHALL compare the same recording and playback configuration with waveform-only and TNK-enabled playback and SHALL report both playback responsiveness and algorithm result lag.

#### Scenario: Performance acceptance is evaluated
- **WHEN** the TNK playback path is compared with a waveform-only baseline
- **THEN** the report includes per-stage distributions, result-window lag, delivered-frame coverage, and queue saturation
- **AND** it distinguishes measured desktop behavior from automated test results
