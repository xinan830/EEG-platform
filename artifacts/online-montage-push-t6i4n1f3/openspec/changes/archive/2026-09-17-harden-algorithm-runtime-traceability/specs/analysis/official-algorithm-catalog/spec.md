## MODIFIED Requirements

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
