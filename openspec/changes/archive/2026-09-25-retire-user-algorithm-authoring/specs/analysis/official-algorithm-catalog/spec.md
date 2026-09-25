# analysis/official-algorithm-catalog Specification

## MODIFIED Requirements

### Requirement: Backend exposes an authoritative official-algorithm catalog

`GET /api/algorithms` SHALL list official Runtime algorithms only, with
backend-authored identity, availability, parameters and output schema. A
catalog error SHALL NOT present a retired user algorithm as runnable.

#### Scenario: Read the catalog

- **WHEN** a client requests `/api/algorithms`
- **THEN** it receives official algorithms and backend-authored input metadata

#### Scenario: All installed official algorithms are listed

- **WHEN** a client requests `GET /api/algorithms`
- **THEN** every platform official algorithm is listed, including disabled
  BrainBeat when its executor is not installed

#### Scenario: Installed evidence is unavailable

- **WHEN** the runtime registry cannot be resolved
- **THEN** the response includes `OFFICIAL_ALGORITHM_CATALOG_UNAVAILABLE`
- **AND THEN** no incomplete official item is represented as runnable

#### Scenario: Read catalog after RBP and FAA enablement

- **WHEN** a client requests `/api/algorithms`
- **THEN** RBP and FAA are runnable with Chinese labels, while BrainBeat
  remains disabled for shadow validation

#### Scenario: Read a runnable official catalog item

- **WHEN** a client reads a runnable official item
- **THEN** its identity and scientific version match the persisted Run

#### Scenario: Read the catalog after user authoring retirement

- **WHEN** a client requests `/api/algorithms`
- **THEN** every listed entry has `source=official`
- **AND THEN** a historical user Definition remains available only from the
  read-only Definition API and historical Run/result resources

### Requirement: Official algorithm availability is not inferred by clients

The frontend SHALL render the official algorithm catalog and its runnable
status from backend metadata. It SHALL NOT expose user graph authoring or
submit retired user-defined algorithm Runs.

#### Scenario: Catalog failure does not hide user algorithms

- **WHEN** the official catalog request fails
- **THEN** historical user Definitions remain readable through their
  read-only API, but are not offered as runnable fallback choices

#### Scenario: Official catalog unavailable

- **WHEN** the official catalog cannot be resolved
- **THEN** the frontend shows a scoped failure and offers no fallback retired
  algorithm as an executable choice
