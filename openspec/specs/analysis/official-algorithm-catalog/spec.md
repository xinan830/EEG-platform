# analysis/official-algorithm-catalog Specification

## Purpose
TBD - created by archiving change modularize-official-algorithm-boundaries. Update Purpose after archive.
## Requirements
### Requirement: Backend exposes an authoritative official-algorithm catalog

The system SHALL expose a read-only official algorithm catalog from a single backend registry. Every entry SHALL include a stable algorithm id, Chinese display name, abbreviation, purpose, Definition id, published version, execution kind, scientific version, implementation identity, availability, runnable state, required logical channel roles, and supported modes.

#### Scenario: All installed official algorithms are listed

- **WHEN** a client requests `GET /api/official-algorithms` after platform initialization
- **THEN** the response lists RBP, Theta/Beta, FAA, BrainBeat, and IAPF
- **AND THEN** IAPF and Theta/Beta report their registry-owned runnable state,
  while shadow-only official algorithms remain non-runnable.

#### Scenario: Installed evidence is unavailable

- **WHEN** an official Definition or its published version cannot be resolved
- **THEN** the catalog returns the structured error code `OFFICIAL_ALGORITHM_CATALOG_UNAVAILABLE`
- **AND THEN** no incomplete item is represented as runnable.

### Requirement: Official algorithm availability is not inferred by clients

The frontend SHALL render official algorithm label, purpose, status, and disabled state from the catalog response. It SHALL NOT derive them from an algorithm name, graph, quality rules, or Definition owner field.

#### Scenario: Catalog failure does not hide user algorithms

- **WHEN** the official catalog request fails while the user Definition request succeeds
- **THEN** the user algorithm list remains available
- **AND THEN** the official section presents a scoped catalog failure message.

### Requirement: Official algorithm refactoring preserves frozen contracts

The system SHALL retain compatibility imports for existing official calculation entry points and SHALL preserve their numerical, unit, channel-order, quality, null, and timing semantics.

#### Scenario: Missing required channel role

- **WHEN** an official algorithm requires Fz/Pz/Oz or F3/F4 roles and the caller does not provide an explicit mapping
- **THEN** it is unavailable with a structured reason
- **AND THEN** the system does not infer Oz from O2 or from channel order.

