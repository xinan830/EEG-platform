## ADDED Requirements

### Requirement: Report measured analysis progress
The system SHALL persist processed and total dynamic windows, including rejected
windows. It SHALL retain the last progress on cancellation/failure and report
full progress on cache completion. Playback time SHALL NOT be execution progress.

#### Scenario: Cancel a dynamic Run
- **WHEN** cancellation is observed between windows
- **THEN** the Run ends cancelled with its last processed count and no result artifact

### Requirement: Merge identical active configurations atomically
The system SHALL merge identical active scientific configuration and execution
identity within the same recording/project/batch scope atomically. Explicit keys
SHALL reject changed scientific inputs. Terminal work SHALL NOT block a new
unkeyed analysis attempt.

#### Scenario: Concurrent identical submissions
- **WHEN** two clients submit the same unkeyed configuration concurrently
- **THEN** both receive one active Run identity

### Requirement: Bound execution concurrency and isolate batch work
The system SHALL run at most two execution workers with bounded native numerical
threads and distinct interactive/batch lanes.
Static interactive Runs SHALL use the interactive lane. Dynamic and project
batch Runs SHALL use the batch lane. Two dynamic Runs MAY remain serialized.

#### Scenario: Interactive work during a batch
- **WHEN** a batch occupies its lane and an interactive Run is queued
- **THEN** the interactive lane can complete that Run independently

### Requirement: Bound raw recording and preprocessing memory
The system SHALL read long recordings by sample blocks and use owned temporary
memory maps for continuous preprocessing. Chunk boundaries SHALL NOT restart
filter state or alter scientific sample coordinates.

#### Scenario: A recording contains a gap
- **WHEN** blocks on either side of the gap are read
- **THEN** unavailable samples remain non-finite and are not silently interpolated
