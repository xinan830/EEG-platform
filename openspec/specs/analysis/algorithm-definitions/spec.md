# analysis/algorithm-definitions Specification

## Purpose
Define immutable, versioned and safely executable backend algorithm definitions
that compose approved research primitives without modifying current official
EEG result paths.
## Requirements
### Requirement: Persist immutable published definitions

The system SHALL store an algorithm identity and SemVer versions whose published
graph, parameters, input/output units, quality rules and references cannot be
modified in place.

#### Scenario: Publish a correction
- **WHEN** a published definition requires a changed parameter or graph
- **THEN** the system SHALL require a new SemVer version and retain the prior version unchanged

### Requirement: Validate safe closed graphs

The system SHALL reject unknown nodes, cycles, missing bindings, incompatible
types or units, missing required channels and invalid parameters before
execution.

#### Scenario: Reject a cycle
- **WHEN** graph edges form a cycle
- **THEN** execution SHALL not start and return structured code `GRAPH_CYCLE`

### Requirement: Restrict formulas

The system SHALL only accept parsed named inputs, numeric constants,
parentheses, approved arithmetic and whitelisted functions; it SHALL never
execute arbitrary Python.

#### Scenario: Reject attribute access
- **WHEN** a formula contains an attribute, import, index or unknown function
- **THEN** validation SHALL fail with `FORMULA_INVALID`

### Requirement: Preserve quality and provenance at execution

The executor SHALL retain each primitive's quality and provenance. Unavailable
scientific values SHALL remain unavailable and shall not become numerical zero.

#### Scenario: Divide by zero
- **WHEN** a graph divides by a zero-valued scalar
- **THEN** the output SHALL be unavailable with reason `division_by_zero`

### Requirement: Preserve legacy result compatibility

The introduction of definitions SHALL not remove or alter current spectrum,
spectrogram, Viewer, or official algorithm API behaviour.

#### Scenario: Request frozen spectrum
- **WHEN** a caller uses `/spectrum/configured`
- **THEN** it SHALL continue to use `offline-spectral-v3`, independent of draft definitions

### Requirement: Definition capabilities report supported primitive and official execution types

The system SHALL return supported primitive nodes, units, and official execution kinds from `GET /api/algorithm-definitions/capabilities`. Official execution kinds SHALL be derived from the official algorithm registry rather than a separate route-local mapping.

#### Scenario: Capability and catalog execution kinds agree

- **WHEN** a client loads both Definition capabilities and the official algorithm catalog
- **THEN** each official algorithm id has the same execution kind in both responses.
