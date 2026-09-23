## ADDED Requirements

### Requirement: Review checkpoint anchors preserve exact causal history

The desktop SHALL resume Python-owned causal filtering only from a completed
checkpoint whose recording identity, raw-manifest identity, sampling rate,
recorded source-channel order and V unit, filter settings, Python filter
contract, contiguous-segment origin, and next sample counter exactly match the
requested work. When no compatible anchor exists, it SHALL process every raw
sample from the current contiguous segment origin to the target in sample-
counter order. It SHALL NOT substitute a bounded warm-up interval and describe
the result as causally continuous.

#### Scenario: A compatible earlier anchor exists

- **WHEN** review requests a filtered target after a completed compatible
  anchor in the same contiguous recorded segment
- **THEN** filtering SHALL restore that anchor and process every subsequent raw
  sample through the target in recorded channel order
- **AND** the target output SHALL remain V/float64 until the explicit display
  conversion boundary.

#### Scenario: No compatible anchor exists

- **WHEN** a cold request targets a later position in a contiguous segment
- **THEN** review SHALL begin causal processing at that segment's origin
- **AND** it SHALL process all preceding samples in bounded batches before
  presenting the target as filtered
- **AND** it SHALL NOT claim that a shortened warm-up produces exact
  continuity.

#### Scenario: Recorded history contains a gap

- **WHEN** the processing path reaches a sample-counter discontinuity
- **THEN** review SHALL discard the preceding causal state
- **AND** it SHALL start a new checkpoint chain at the first recorded sample
  after the gap
- **AND** it SHALL not generate or filter fabricated samples for the gap.

### Requirement: Long-record anchor generation is bounded and foreground-aware

The desktop SHALL generate missing checkpoint anchors using bounded raw windows,
bounded concurrency, and coalesced target work. The active user target SHALL
take priority over speculative background generation, while obsolete seek
targets SHALL not create an unbounded queue. The previously completed waveform
SHALL remain visible until the exact target frame is complete.

#### Scenario: A user jumps far into a cold long recording

- **WHEN** no compatible anchor is close enough to satisfy the target directly
- **THEN** review SHALL report that exact filter history is being prepared
- **AND** it SHALL expose progress derived from processed and required sample
  counters rather than wall-clock estimates
- **AND** it SHALL keep the prior complete frame visible
- **AND** it SHALL commit the target position and waveform atomically only when
  the exact target frame is complete.

#### Scenario: The user changes the target during anchor generation

- **WHEN** a newer seek supersedes an unfinished target
- **THEN** review SHALL coalesce foreground work around the latest target
- **AND** reusable completed anchors SHALL remain available
- **AND** obsolete frame results SHALL not replace the latest requested frame.

#### Scenario: Nearby history is prepared in the background

- **WHEN** foreground review is idle and bounded capacity is available
- **THEN** review MAY extend the compatible checkpoint chain in sample-counter
  order
- **AND** foreground navigation, playback, and disposal SHALL preempt or cancel
  that speculative work.

### Requirement: Checkpoint-anchor storage is disposable and validated

The desktop SHALL store only completed checkpoint anchors outside the immutable
recording directory using atomic publication and a versioned compatibility key.
Anchor storage SHALL have explicit size and concurrency bounds. Corrupt,
partial, incompatible, or evicted anchors SHALL be treated as cache misses and
rebuilt from immutable raw samples without changing the raw recording.

#### Scenario: Anchor publication is interrupted

- **WHEN** generation is cancelled or fails before an anchor is fully written
- **THEN** the incomplete entry SHALL not be discoverable as a usable anchor
- **AND** a later request SHALL resume from an earlier completed compatible
  anchor or the contiguous segment origin.

#### Scenario: Filter or recording provenance changes

- **WHEN** any compatibility-key field differs from a stored anchor
- **THEN** review SHALL reject that anchor as a cache hit
- **AND** it SHALL rebuild causal state for the new provenance without deleting
  or rewriting raw data.

#### Scenario: Anchor preparation fails

- **WHEN** raw reading, Python filtering, checkpoint validation, or cache
  publication fails for the active target
- **THEN** review SHALL retain the last complete frame
- **AND** it SHALL expose an explicit failure state that does not claim the
  selected filter was applied to the target.
