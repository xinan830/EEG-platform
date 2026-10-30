## Purpose

Define the bounded, versioned spectral source used by TNK dynamic windows so a
first cursor request does not prepare the complete recording.

## ADDED Requirements

### Requirement: Prepare only the TNK target window and finite context

The TNK dynamic spectral source SHALL read and preprocess only the requested
half-open sample range plus a finite, versioned filter context clamped to the
recording boundaries. It SHALL NOT read or filter the complete recording just
to publish one TNK dynamic window.

#### Scenario: First TNK window on a long recording

- **WHEN** a valid TNK cursor requests its first eligible 4-second window
- **THEN** the source reads only that target range and its finite context, filters
  that bounded source, and returns the target samples with their original
  absolute sample coordinates
- **AND** no complete-recording scientific preparation is started

#### Scenario: Rolling 30-second TNK metric

- **WHEN** an IAPF, HAI, or aperiodic TNK point requests its eligible 30-second
  range
- **THEN** the source reads only that 30-second range and its finite context
- **AND** the returned window duration and absolute start/end remain exactly the
  planned half-open range

### Requirement: Declare bounded preprocessing semantics

Every bounded TNK result SHALL identify its preprocessing scope, finite context
policy, scientific version, filter phase, boundary policy, sample rate, V unit,
channel order, source identity, and actual half-open sample range. The bounded
version SHALL be distinct from the continuous whole-recording version for
cache and provenance purposes.

#### Scenario: Inspect a ready bounded result

- **WHEN** a TNK bounded window is ready
- **THEN** its provenance reports the bounded TNK preprocessing identity,
  context policy, clamping at recording edges, zero-phase filtering, V input
  semantics, ordered channels, and source recording identity
- **AND** an artifact produced by the whole-recording preprocessing identity
  cannot satisfy the bounded identity

### Requirement: Preserve source and quality boundaries

The bounded source SHALL preserve sample-major float64 values in volts, the
declared role-to-channel order, half-open sample coordinates, and the existing
quality gate behavior. Non-finite samples or source gaps in the target or its
filter context SHALL be rejected with structured quality evidence; they SHALL
NOT be filled with zero or silently joined across a gap.

#### Scenario: Gap intersects bounded preparation

- **WHEN** a target or finite filter context contains missing or non-finite
  samples
- **THEN** the TNK window is unavailable or quality-rejected with a structured
  gap reason
- **AND** no numeric replacement value is emitted

### Requirement: Keep non-TNK continuous preprocessing unchanged

The generic offline spectral pipeline SHALL retain its continuous
whole-recording preprocessing semantics. A bounded TNK request SHALL be the
only path that opts into the new bounded preprocessing identity.

#### Scenario: Generic offline PSD request

- **WHEN** a non-TNK offline PSD or spectrogram request is processed
- **THEN** it continues to use the existing continuous preprocessing contract
- **AND** it does not reuse or create a bounded TNK source
