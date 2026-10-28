## Purpose

以 RBP 五频带堆叠图建立统一调度器的第一条可验证实时分析闭环，确保共享窗口准备、频带计算、历史追加和前端发布都遵循同一科学时间和质量契约。

## ADDED Requirements

### Requirement: Compute RBP from one shared prepared window

For each exact current RBP window, the backend SHALL read the authoritative recording projection once and SHALL reuse one common prepared window for all RBP outputs. RBP SHALL preserve the declared sample rate, recording/montage identity, channel order, 1–50 Hz preprocessing, 50 Hz notch and public ratio units.

#### Scenario: First RBP window
- **WHEN** a current RBP target has sufficient samples and a valid source configuration
- **THEN** the backend performs one authoritative window preparation and returns the five named bands with explicit window bounds, units, quality and provenance.

#### Scenario: Repeated exact target
- **WHEN** the same exact target is requested again in the current generation
- **THEN** the backend reuses the in-flight or ready preparation/result
- **AND** it does not read or preprocess the same source interval again.

### Requirement: Append RBP history by absolute window time

The desktop SHALL maintain one bounded history per selected RBP output channel and SHALL append validated values by recording-relative window end time. It SHALL preserve channel order and distinguish missing, rejected and failed windows from numeric zero.

#### Scenario: Results arrive out of order
- **WHEN** two valid RBP windows complete in a different order from their recording times
- **THEN** the history inserts them at their scientific times
- **AND** the stacked chart remains chronologically ordered without moving the playback cursor.

#### Scenario: One channel fails
- **WHEN** one output channel fails quality validation in an otherwise valid window
- **THEN** that channel shows a gap with structured evidence
- **AND** other valid channels in the same window remain renderable.

### Requirement: Keep RBP playback independent from waveform rendering

RBP computation or publication SHALL NOT block waveform frame production, cursor progression or raw recording persistence. If RBP falls behind, the UI SHALL expose a measurable analysis lag or unavailable interval rather than pausing or accelerating playback.

#### Scenario: RBP computation exceeds cadence
- **WHEN** an RBP window takes longer than one playback interval
- **THEN** waveform and cursor continue according to the playback clock
- **AND** superseded RBP targets follow the bounded latest-target policy without unbounded accumulation.

### Requirement: Gate later algorithm migration on RBP acceptance

The system SHALL keep additional TNK real-time algorithm adapters disabled until RBP passes automated contract tests, focused scheduler tests, and real-recording playback acceptance for continuity, resource bounds, shared preparation counts and publication lag.

#### Scenario: RBP acceptance is incomplete
- **WHEN** any required RBP scheduler or real-recording acceptance criterion is incomplete
- **THEN** the system SHALL not claim the unified scheduler is ready for additional TNK indicators
- **AND** existing full-run analysis behavior remains available.
