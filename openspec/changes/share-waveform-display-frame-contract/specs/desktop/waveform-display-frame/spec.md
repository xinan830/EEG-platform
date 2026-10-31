# desktop/waveform-display-frame Specification

## ADDED Requirements

### Requirement: Waveform display requests have a shared identity

Every asynchronous waveform display request MUST identify the local recording session, source revision, view generation, and unique request ID. Review and TNK MUST use the same identity contract while retaining their reader-specific payloads.

#### Scenario: Independent readers produce a frame

- **WHEN** Review or TNK completes a waveform read
- **THEN** the payload is associated with the local recording identity and source revision that were active when the request began
- **AND** the payload keeps its existing reader-specific time, unit, channel, gap, and filter representation

### Requirement: Stale waveform frames are not published

An asynchronous waveform frame MUST be published only when its request identity exactly matches the consumer's current request and the request has not been canceled.

#### Scenario: A newer view request supersedes an older read

- **WHEN** an older read completes after the recording source or view request changes
- **THEN** the older payload is discarded without replacing the current complete frame

#### Scenario: The active request completes

- **WHEN** a complete payload returns for the exact current request
- **THEN** the consumer publishes it through the shared gate
- **AND** Review cache hits continue to use their source- and range-validated cached frame
