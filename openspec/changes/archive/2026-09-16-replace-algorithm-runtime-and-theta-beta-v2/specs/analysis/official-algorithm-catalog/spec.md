## MODIFIED Requirements

### Requirement: Backend exposes an authoritative official-algorithm catalog

The system SHALL expose official and user algorithms through `GET
/api/algorithms`.  Each entry SHALL identify its source, version, availability,
plain-language label/purpose, supported modes, output schema, and client-safe
parameter schema.

#### Scenario: Read the catalog

- **WHEN** a client requests `/api/algorithms`
- **THEN** it can distinguish official from user algorithms and render each
selected algorithm's inputs without inspecting internal DAG or adapter data

#### Scenario: All installed official algorithms are listed

- **WHEN** a client requests `GET /api/algorithms`
- **THEN** the response lists every runtime-registered official algorithm
- **AND THEN** IAPF and Theta/Beta v2 report their runtime-owned runnable
  state without an installed Definition prerequisite.

#### Scenario: Installed evidence is unavailable

- **WHEN** the runtime registry cannot be resolved
- **THEN** the response returns user algorithms plus
  `OFFICIAL_ALGORITHM_CATALOG_UNAVAILABLE`
- **AND THEN** no incomplete official item is represented as runnable.

## ADDED Requirements

### Requirement: Keep non-runnable official algorithms unavailable

The catalog SHALL expose FAA and BrainBeat as non-runnable until separately
validated and enabled.  IAPF and Theta/Beta v2 SHALL expose their own raw
channel parameter schemas and SHALL NOT declare required global roles.

#### Scenario: Read Theta/Beta catalog metadata

- **WHEN** a client reads the Theta/Beta catalog entry
- **THEN** it describes one selected raw channel and contains no Fz/Pz/Oz
  mapping prerequisite
