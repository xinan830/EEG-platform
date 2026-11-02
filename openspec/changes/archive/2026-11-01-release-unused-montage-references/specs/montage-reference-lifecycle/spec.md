## ADDED Requirements

### Requirement: Existing references govern preset deletion
The desktop SHALL prohibit deleting a user montage while an existing recording
or recoverable trashed recording references it. After the last discoverable
reference is permanently removed and verification succeeds, it SHALL allow
deleting the preset, including an archived preset, without rewriting raw data.

#### Scenario: Last recording is permanently deleted
- **GIVEN** a user montage is referenced by one recording
- **WHEN** the recording directory is permanently removed and references refresh
- **THEN** preset deletion is allowed after restart as well
- **AND** the historical ID still cannot be overwritten or reused

#### Scenario: Record is moved to trash
- **WHEN** a referenced recording is soft-deleted into the project trash
- **THEN** its montage remains protected from deletion

#### Scenario: Multiple remaining records
- **WHEN** one of several referencing records is permanently deleted
- **THEN** the remaining records continue to prohibit preset deletion

### Requirement: Reference locations are durable and verified
The desktop SHALL persist known project roots and registered recording locations.
Removing a project from the list SHALL NOT release references to its remaining
files. Unreadable metadata, unavailable volumes and unlocated new references
SHALL NOT be treated as proof that recordings were deleted.

#### Scenario: External record survives project removal
- **WHEN** the project is removed or the desktop restarts
- **THEN** registered existing recording locations continue to protect montages

#### Scenario: Reference verification fails
- **WHEN** an indexed recording volume is unavailable or its manifest is corrupt
- **THEN** deletion is refused with an explicit verification error

### Requirement: Legacy ID locks are migrated without losing provenance
Legacy ID-only indices SHALL retain historical edit locks while rebuilding
deletion locks from discoverable recordings and trash. Unrecoverable old external
locations SHALL be documented as a migration visibility limit, not silently
claimed as verified. Historical snapshots SHALL remain unchanged.

#### Scenario: Orphaned legacy lock
- **GIVEN** an old ID-only lock has no remaining discoverable recording
- **WHEN** migration and scanning succeed
- **THEN** preset deletion is allowed but editing the old ID remains forbidden

### Requirement: Recording publication protects in-flight references
Preset mutation and recording publication SHALL share a serialization boundary.
A concurrent delete SHALL NOT bypass protection between reference registration
and publication of a complete manifest.

#### Scenario: Delete during record creation
- **WHEN** preset deletion is requested while a recording manifest is being published
- **THEN** deletion waits for publication and rejects the existing reference
