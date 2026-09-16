## MODIFIED Requirements

### Requirement: Backend exposes an authoritative official-algorithm catalog

The official catalog SHALL mark RBP and FAA runnable only after their backend
modules, client-safe parameter schemas, Run serialization and regression tests
are installed. BrainBeat SHALL remain non-runnable while its stateful realtime
contract is not represented by a traceable Run.

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
