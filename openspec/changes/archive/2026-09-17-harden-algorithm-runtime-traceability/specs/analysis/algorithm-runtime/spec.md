## MODIFIED Requirements

### Requirement: Execute every selectable algorithm through the runtime

The system SHALL resolve every selectable algorithm to one concrete runtime
module and scientific version before queueing. Generic Run lifecycle and
execution services SHALL NOT branch on a concrete algorithm ID. The resolved
scientific version SHALL be persisted and passed unchanged to execution.

#### Scenario: Run a registered algorithm

- **WHEN** a client submits a valid selected algorithm version and config
- **THEN** the runtime validates that module's configuration and raw channel
  selections, executes its static or dynamic method, and persists the returned
  result, actual ranges, quality, implementation identity, and artifacts

#### Scenario: Request an unknown or unavailable version

- **WHEN** a client submits an unknown algorithm identity or unavailable
  version
- **THEN** the system returns a structured error and creates no completed Run

#### Scenario: Queue a resolved official algorithm

- **WHEN** a client submits an official algorithm Run without a client-supplied
  Definition identity
- **THEN** the backend resolves and persists the platform Definition ID,
  published Definition version, algorithm scientific version and module digest
- **AND THEN** the queue executes that exact scientific version

#### Scenario: Multiple installed scientific versions

- **WHEN** more than one scientific version is registered for an algorithm ID
- **THEN** an unspecified version is rejected unless one active version is
  explicitly declared
- **AND THEN** a resolved version is never replaced by a lexical latest-version
  lookup during execution

## ADDED Requirements

### Requirement: Module-owned execution evidence

Each executable module SHALL supply a serializable execution snapshot for its
window, preprocessing and quality contract. The runtime SHALL validate common
provenance evidence and module extension evidence before persisting a completed
Run.

#### Scenario: Persist FAA execution snapshot

- **WHEN** FAA completes a Run
- **THEN** the Run stores its paired-epoch processing and quality snapshot from
  the FAA module
- **AND THEN** the generic Run service contains no FAA-specific branch
