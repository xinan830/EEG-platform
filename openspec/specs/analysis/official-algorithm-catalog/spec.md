# analysis/official-algorithm-catalog Specification

## Purpose
TBD - created by archiving change modularize-official-algorithm-boundaries. Update Purpose after archive.
## Requirements
### Requirement: Backend exposes an authoritative official-algorithm catalog

The official catalog SHALL derive runnable official metadata from the installed
runtime module manifest, including Definition identity, scientific version,
implementation identity, output unit, modes and availability. A non-runnable
official capability without a runtime module MAY use one explicit static
descriptor.

#### Scenario: Read the catalog

- **WHEN** a client requests `/api/algorithms`
- **THEN** it can distinguish official from user algorithms and render each
  selected algorithm's inputs without inspecting internal DAG or adapter data

#### Scenario: All installed official algorithms are listed

- **WHEN** a client requests `GET /api/algorithms`
- **THEN** the response lists every platform official algorithm, including a
  disabled BrainBeat entry when its executor is not installed
- **AND THEN** executable items report their runtime-owned runnable state
  without an installed Definition prerequisite.

#### Scenario: Installed evidence is unavailable

- **WHEN** the runtime registry cannot be resolved
- **THEN** the response returns user algorithms plus
  `OFFICIAL_ALGORITHM_CATALOG_UNAVAILABLE`
- **AND THEN** no incomplete official item is represented as runnable.

#### Scenario: Read catalog after RBP and FAA enablement

- **WHEN** a client requests `/api/algorithms`
- **THEN** RBP and FAA are available and runnable with readable Chinese labels
  and declared input fields
- **AND THEN** BrainBeat remains `shadow_validation` and disabled

#### Scenario: Read a runnable official catalog item

- **WHEN** a client reads `/api/algorithms`
- **THEN** each runnable official item exposes the same identity and version
  that will be persisted and executed for a Run
- **AND THEN** the client does not need to infer an identity from the algorithm
  name or a duplicated catalog mapping

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

### Requirement: Keep non-runnable official algorithms unavailable

The catalog SHALL expose FAA and BrainBeat as non-runnable until separately
validated and enabled.  IAPF and Theta/Beta v2 SHALL expose their own raw
channel parameter schemas and SHALL NOT declare required global roles.

#### Scenario: Read Theta/Beta catalog metadata

- **WHEN** a client reads the Theta/Beta catalog entry
- **THEN** it describes one selected raw channel and contains no Fz/Pz/Oz
  mapping prerequisite

