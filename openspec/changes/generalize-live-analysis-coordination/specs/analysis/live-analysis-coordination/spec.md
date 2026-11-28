# Spec Delta

## Purpose

Defines one live-analysis lifecycle and scheduling contract for algorithms that consume acquisition-session samples. It keeps scientific calculation and offline Run execution owned by their existing algorithm and review boundaries.

## ADDED Requirements

### Requirement: Coordinate live algorithms by acquisition session

The backend SHALL manage live algorithm sessions by acquisition session identity and explicit algorithm identity. Starting, stopping, or replacing one algorithm SHALL NOT reuse another session's samples or scientific state, and closing acquisition SHALL release all associated live algorithm state.

#### Scenario: Start a second algorithm in one acquisition session
- **WHEN** a supported second algorithm is enabled for an active acquisition session
- **THEN** it receives its own validated configuration and live result identity under that session
- **AND** the desktop capture lifecycle and offline analysis coordinator remain unchanged

#### Scenario: A stale session sends samples
- **WHEN** a live request references a session that has been stopped or replaced
- **THEN** the coordinator returns a structured stale-session failure
- **AND** it publishes no result for the active session

### Requirement: Schedule windows on canonical sample coordinates

Windowed live algorithms SHALL receive windows defined by actual sampling rate, ordered channel names, units, and half-open sample-counter bounds. Scheduling SHALL be independent of request arrival time and SHALL preserve explicit gaps and unavailable ranges without interpolation or zero filling.

#### Scenario: Samples arrive in variable transport batches
- **WHEN** contiguous samples arrive in batches of different sizes
- **THEN** each algorithm receives the same configured sample-aligned windows
- **AND** result time bounds are derived from those sample coordinates and sampling rate

#### Scenario: A window crosses a sample gap
- **WHEN** a scheduled window overlaps a missing or overwritten interval
- **THEN** the coordinator reports that window as unavailable with its actual bounds
- **AND** later windows resume only from a contiguous valid range

### Requirement: Bound scheduling and reject stale results

The live coordinator SHALL bound pending work and retain only the current session's latest result per algorithm. Overload, cancellation, worker failure, and out-of-order completion SHALL be explicit; a stale completion SHALL NOT replace a newer result.

#### Scenario: An algorithm cannot keep up
- **WHEN** pending work reaches its configured capacity
- **THEN** affected windows are marked unavailable or overloaded with a stable code
- **AND** acquisition and raw persistence continue

#### Scenario: An older window completes later
- **WHEN** a result for an earlier sample range completes after a newer range
- **THEN** it is discarded or retained only as non-current history according to the result contract
- **AND** it is not exposed as the latest result

### Requirement: Keep algorithm science in its adapter

Each live algorithm SHALL declare its input/configuration constraints and implement calculation for a validated live window. The coordinator SHALL own lifecycle and scheduling only; it SHALL NOT branch on algorithm identity to implement scientific formulas, preprocessing, or quality rules.

#### Scenario: RBP is scheduled by the common coordinator
- **WHEN** the coordinator dispatches a valid RBP window
- **THEN** the RBP adapter returns its existing live scientific identity, values, units, quality, and provenance
- **AND** those fields remain equivalent to the pre-coordination RBP contract for the same input window

#### Scenario: An algorithm config is invalid
- **WHEN** an algorithm's declared validator rejects its configuration or input requirements
- **THEN** the coordinator returns a typed failure for that algorithm session
- **AND** it does not alter acquisition or other algorithm sessions

### Requirement: Preserve live result envelopes and ephemeral ownership

Every live update SHALL identify its message type, schema version, acquisition session, algorithm, implementation, sample/time bounds, state, units, quality, and failure code when applicable. Live results SHALL remain ephemeral and SHALL NOT be stored as offline Runs or backtrace artifacts.

#### Scenario: A desktop receives an update for another algorithm
- **WHEN** a live update arrives with a known algorithm identity and current schema
- **THEN** the desktop routes it to that algorithm's presenter without parsing algorithm-specific values in acquisition lifecycle code
- **AND** stale session or revision updates are ignored

#### Scenario: An unsupported algorithm is requested
- **WHEN** a client requests an algorithm not registered for live execution
- **THEN** the service returns a stable unsupported-algorithm failure
- **AND** it creates no session or persisted result

### Requirement: Start live RBP from the rolling acquisition window

During preview or recording, the acquisition client SHALL feed accepted raw EEG samples to the bounded live source from the first batch of the acquisition session. RBP enablement SHALL use the latest sample already received by that source as the first window endpoint when enough acquisition time has elapsed. RBP SHALL keep its shared five-band calculation, 10-second default window, and 1-second default step. Old samples SHALL be overwritten according to retention capacity.

#### Scenario: Enable RBP at 23 seconds
- **WHEN** the latest accepted sample range ends at acquisition time 23 seconds and `[13s, 23s)` is retained and valid
- **THEN** the enable command immediately schedules `[13s, 23s)` and the first result uses that range
- **AND** the next regular result uses `[14s, 24s)` after its final sample arrives

#### Scenario: Enable RBP before 10 seconds are available
- **WHEN** the user enables RBP at acquisition time 4 seconds with contiguous samples from the session start
- **THEN** the UI indicates warming up and the first result is calculated for `[0s, 10s)` as soon as that window is available

#### Scenario: The latest 10-second window contains a gap
- **WHEN** the newest requested window contains missing input or is overwritten before the worker reads it
- **THEN** the affected window reports its actual bounds and a structured unavailable reason
- **AND** regular updates resume with the next valid complete window

#### Scenario: Enable RBP again after the buffer rolls over
- **WHEN** RBP is turned off and then on within the same acquisition session after older samples have been overwritten
- **THEN** the latest retained full window is scheduled and `start_s` / `end_s` still use the acquisition session's first accepted sample as time zero
- **AND** stopping the acquisition closes the cache and rejects results from the old session

### Requirement: Preserve live scalar channel roles and processing provenance

Live scalar algorithms SHALL validate role-specific inputs before calculation and expose the processing scope and filter parameters with each result. The desktop SHALL make material differences from offline preprocessing visible to the user.

#### Scenario: BrainBeat receives Fz and Pz source channels
- **WHEN** a live BrainBeat configuration is submitted
- **THEN** its primary and secondary channels SHALL satisfy the same supported Fz/Pz source-role contract as the offline BrainBeat algorithm
- **AND** bipolar derivations SHALL NOT be silently accepted as Fz or Pz

#### Scenario: A live scalar result uses window-local preprocessing
- **WHEN** a scalar result is displayed after window-local zero-phase filtering
- **THEN** the result details SHALL show the filter band, notch setting, and that the live window is not equivalent to offline continuous preprocessing

#### Scenario: A bipolar formula uses an allowed BrainBeat channel name
- **WHEN** a montage output has a supported BrainBeat source name but its formula uses a single-channel or specified-pair reference, or a different positive electrode
- **THEN** the desktop SHALL exclude that output from BrainBeat choices and the backend SHALL reject it with `LIVE_CONFIG_INVALID`
- **AND** hardware-reference outputs with no negative inputs and mean-reference outputs with at least two distinct negative inputs SHALL remain supported for the matching positive electrode
- **AND** montage outputs missing reference-kind metadata SHALL NOT be accepted as BrainBeat source channels

### Requirement: Keep the IAPF live trend scale stable

The live IAPF trend chart SHALL use a fixed 8–13 Hz vertical range independent of the backend 7–13 Hz search range with an 8–13 Hz classic Alpha band highlight and optional COG reference line so incoming values cannot cause automatic axis rescaling.

#### Scenario: IAPF values move below 8 Hz
- **WHEN** a valid IAPF result is below 8 Hz
- **THEN** the chart vertical range SHALL remain 8–13 Hz
- **AND** the latest numeric result SHALL remain visible independently of the plotted range
