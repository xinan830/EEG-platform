## Purpose

为历史回放和实时预览提供一个统一、有界且可观测的分析调度契约，使游标、波形、窗口算法和结果发布使用同一时间来源，并在计算过载时保持可预测行为。

## ADDED Requirements

### Requirement: Use one playback clock and target stream

The system SHALL derive waveform display targets and analysis window targets from one recording-relative playback clock. Algorithms and charts SHALL NOT advance an independent playback position.

#### Scenario: Continuous playback advances
- **WHEN** the playback clock advances to a new recording-relative sample position
- **THEN** the scheduler emits at most one current target for each subscribed task class
- **AND** waveform and analysis targets carry the same recording identity, sample rate, channel order, and cursor position.

#### Scenario: Seek or context change
- **WHEN** the user seeks, changes recording/montage/configuration, or restarts playback
- **THEN** the scheduler creates a new context generation
- **AND** queued targets from the prior generation cannot publish into the new history.

### Requirement: Bound and classify work

The scheduler SHALL use finite capacity and explicit policies for waveform preview, window-independent analysis, stateful analysis, and batch analysis. It SHALL expose queued, running, completed, cancelled, rejected, and failed states without silently creating an unbounded backlog.

#### Scenario: Analysis is slower than playback
- **WHEN** accepted analysis work takes longer than the playback cadence
- **THEN** the scheduler coalesces or cancels superseded window-independent targets according to policy
- **AND** waveform targets continue independently
- **AND** the queue depth remains within its configured bound.

#### Scenario: Capacity is exhausted
- **WHEN** admitting work would exceed the queue or resource budget
- **THEN** the scheduler returns a structured capacity outcome
- **AND** it does not increase concurrency implicitly or discard raw recording data.

### Requirement: Deduplicate preparation and in-flight work

For an exact recording, montage, configuration, window policy, and half-open sample range, the scheduler SHALL share one in-flight preparation and computation where the task contract permits. A cache hit SHALL avoid repeating raw reads and common preparation.

#### Scenario: Two consumers request one RBP window
- **WHEN** waveform-adjacent RBP and another authorized consumer request the same exact window
- **THEN** they observe one shared prepared window and one RBP computation
- **AND** each consumer may independently stop observing the result.

### Requirement: Publish results by scientific time

The scheduler SHALL publish only validated results carrying source identity, montage/channel order, sample range, units, algorithm version, quality and provenance. Consumers SHALL merge results by recording-relative window end time and generation, never by task completion order.

#### Scenario: An older task finishes after a newer task
- **WHEN** an older window completes after a newer window
- **THEN** the result is inserted at its scientific time if the generation is current
- **AND** it cannot overwrite a newer value or shift the cursor.

#### Scenario: A window is unavailable
- **WHEN** a window lacks sufficient samples, contains a source gap, or is scientifically rejected
- **THEN** the scheduler publishes a structured non-ready state or null value with evidence
- **AND** it does not emit numeric zero or silently omit the reason.

### Requirement: Make lifecycle diagnostics complete

The scheduler SHALL record one trace identity per target and timestamps for queued, prepared, computed, published, cancelled and failed transitions, including queue wait, compute duration, result lag and replayed sample counts.

#### Scenario: Diagnose a lagging playback
- **WHEN** an operator enables developer diagnostics
- **THEN** the trace can correlate one cursor target with its data preparation, algorithm execution and chart publication
- **AND** ordinary research view remains free of diagnostic details.
