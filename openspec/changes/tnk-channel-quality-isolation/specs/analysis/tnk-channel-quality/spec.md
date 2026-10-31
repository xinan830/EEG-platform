# TNK channel quality isolation

## ADDED Requirements

### Requirement: Isolate a shared-spectrum quality rejection by channel
When a TNK multi-channel bounded spectrum request fails its quality gate, the
system MUST retry the same sample range per mapped channel using the same
quality contract.

#### Scenario: One mapped channel is rejected
- **WHEN** one mapped channel exceeds the quality gate and the other mapped
  channels pass
- **THEN** the valid channels remain available to children that only consume
  those channels
- **AND** the rejected channel carries `TNK_QUALITY_REJECTED` evidence
- **AND** no zero, interpolated, or previous value is emitted

#### Scenario: Every mapped channel is rejected
- **WHEN** every mapped channel fails the quality gate
- **THEN** the TNK window remains unavailable under the existing structured
  quality failure contract

### Requirement: Preserve the existing clean fast path
A clean multi-channel TNK window MUST continue to use one shared spectrum load
and MUST produce the same scientific values and provenance as before.

#### Scenario: All mapped channels pass
- **WHEN** the shared multi-channel quality gate passes
- **THEN** TNK performs one shared spectrum load for that range
- **AND** the returned values and provenance remain unchanged
