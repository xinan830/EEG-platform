## ADDED Requirements

### Requirement: Definition capabilities report supported primitive and official execution types

The system SHALL return supported primitive nodes, units, and official execution kinds from `GET /api/algorithm-definitions/capabilities`. Official execution kinds SHALL be derived from the official algorithm registry rather than a separate route-local mapping.

#### Scenario: Capability and catalog execution kinds agree

- **WHEN** a client loads both Definition capabilities and the official algorithm catalog
- **THEN** each official algorithm id has the same execution kind in both responses.
