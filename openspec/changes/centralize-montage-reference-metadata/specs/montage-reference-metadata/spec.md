## ADDED Requirements

### Requirement: Shared recording montage metadata interpretation

The desktop scanner and preset store SHALL use one internal typed adapter to
interpret the recording's montage ID and snapshot fields, retaining the
existing dictionary-shaped manifest and public registration APIs. This
adapter SHALL NOT change time, units, channel order, signal processing,
reference locks or immutable recording provenance.

#### Scenario: Legacy ID-only metadata
- **WHEN** recording metadata contains only a nonblank montage configuration ID
- **THEN** that exact case-sensitive ID is collected without rewriting metadata

#### Scenario: Blank snapshot during historical discovery
- **WHEN** a historical snapshot field is blank
- **THEN** discovery ignores that snapshot and retains any nonblank metadata ID

#### Scenario: Blank snapshot during strict acquisition validation
- **WHEN** strict acquisition validation receives a nonblank montage ID and a present blank snapshot field
- **THEN** validation fails without registering a montage reference

#### Scenario: Conflicting or malformed snapshot
- **WHEN** a nonblank snapshot is malformed, lacks its ID or conflicts with the metadata ID
- **THEN** reference verification fails instead of silently falling back
