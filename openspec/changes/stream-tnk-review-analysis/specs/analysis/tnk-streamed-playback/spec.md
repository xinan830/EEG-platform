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

#### Scenario: Playback target supersedes an active stale replay
- **WHEN** a newer playback target arrives while the active TNK replay has exceeded the playback step
- **THEN** the playback lane requests cooperative cancellation of the superseded active observation
- **AND** the backend stops at a cancellation checkpoint and releases its subscription and compute slot
- **AND** the replacement target restores a compatible checkpoint and replays every required canonical frame through its target
- **AND** the waveform clock remains independent of cancellation and replacement

#### Scenario: Waveform refreshes overlap with on-demand analysis
- **WHEN** a seek, role change, or filter change occurs while a waveform preview request is still running
- **THEN** the desktop coalesces pending refreshes and does not start a second preview request before the current one finishes
- **AND** a stale response is discarded by its source revision
- **AND** concurrent requests for the same continuous prepared source share one backend preprocessing operation

#### Scenario: TNK cursor is idle before playback
- **WHEN** a TNK on-demand session is created and the review cursor has not queued a playback target
- **THEN** the desktop does not start an idle playback-event polling loop
- **AND** the initial target is obtained through the existing target-only request path
- **AND** ordered event consumption starts when playback advances or queues a playback target

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

### Requirement: Share migrated ANT time-series preprocessing within a session
The four migrated ANT time-series children SHALL read each newly required raw Fz
sample range once per TNK replay stream. Crimson and Zenlite MAY maintain separate
resampling and filter states because their scientific contracts differ. Zenlite
calmness, awareness, and spectral relaxation SHALL share one Zenlite preprocessing
and model state and one model inference per canonical frame. A seek, scientific
context change, or replay reset SHALL clear the stream state before rebuilding it.

#### Scenario: Continuous playback advances one canonical frame
- **WHEN** the next TNK frame extends the previous replay target by one second
- **THEN** the raw Fz source is read only for the new sample suffix
- **AND** the migrated ANT children consume the shared session stream
- **AND** the result provenance identifies the source chunk and preprocessing contract

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

### Requirement: Append on-demand STFT columns by analysis time
The desktop SHALL retain accepted on-demand STFT columns in recording-absolute
time order. A later overlapping window SHALL contribute only columns whose
absolute time is not already present; it SHALL not replace the existing
history with the latest window. The frequency axis, units, quality and
missing-cell semantics SHALL remain those returned by the backend.

#### Scenario: A later STFT window overlaps the displayed history
- **WHEN** the playback cursor advances and the next STFT window overlaps the previous window
- **THEN** the desktop appends only the new absolute-time columns to the heatmap
- **AND** previously displayed columns remain visible at their original times

#### Scenario: STFT context moves backward or changes
- **WHEN** the user seeks backward, changes the recording/channel/frequency/notch configuration, or starts a new on-demand session
- **THEN** the STFT history is cleared before accepting the new context
- **AND** the new window becomes the first column set in the rebuilt projection

### Requirement: Measure playback and TNK stages independently
The system SHALL expose bounded diagnostics for waveform tick interval/handler duration, queue wait/depth, source preparation, state replay/computation, event delivery, history projection, and chart update. Performance acceptance SHALL compare the same recording and playback configuration with waveform-only and TNK-enabled playback and SHALL report both playback responsiveness and algorithm result lag.

#### Scenario: Performance acceptance is evaluated
- **WHEN** the TNK playback path is compared with a waveform-only baseline
- **THEN** the report includes per-stage distributions, result-window lag, delivered-frame coverage, and queue saturation
- **AND** it distinguishes measured desktop behavior from automated test results
