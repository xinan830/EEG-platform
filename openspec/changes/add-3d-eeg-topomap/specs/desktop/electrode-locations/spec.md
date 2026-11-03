## ADDED Requirements

### Requirement: Standard 3D electrode locations resolve through an explicit GNM mapping
The system SHALL provide a typed read-only 3D location resolution path for exact `colin27_1020` electrode labels. A mapped result SHALL include the authoritative MNE source identity and coordinates, mapping identity, target GNM model identity, target coordinates, and explicit source/target frames and units. The existing 2D resolution and manual diagram positions SHALL remain independent. A missing or invalid 3D mapping SHALL return a structured unavailable result and MUST NOT fall back to `MapX`/`MapY` or an approximate mapping.

#### Scenario: Resolve an electrode supported by the standard mapping
- **WHEN** a requested label exactly matches a supported MNE site and the mapping and model identities validate
- **THEN** the API returns its GNM coordinates and complete source/target provenance in request order

#### Scenario: Resolve an unsupported or unmapped label
- **WHEN** a requested label is blank, unsupported, REF/GND, or absent from the mapping
- **THEN** its 3D location is unavailable with a reason and no coordinate is inferred

#### Scenario: Mapping assets are incompatible
- **WHEN** the source frame/unit cannot be related explicitly to the platform's MNE coordinates or the mapping references another GNM mesh
- **THEN** the API returns a stable structured error/unavailable status and leaves the existing 2D location behavior unchanged
