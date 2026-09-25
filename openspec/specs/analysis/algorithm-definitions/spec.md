# analysis/algorithm-definitions Specification

## Purpose

Persist immutable Definition identities and versions used as provenance for
official Runtime algorithms. This is an internal contract, not a user formula
builder or executable extension point.

## Requirements

### Requirement: Persist immutable official definitions

The system SHALL store the identity and SemVer version of each official
algorithm used by a Run. Published graph, parameters, input/output units,
quality rules, and references SHALL be immutable.

#### Scenario: Official Run records its Definition identity

- **WHEN** an official algorithm Run is created
- **THEN** the Run stores the official Definition id, version, and digest
- **AND THEN** the Definition is resolved from the Runtime catalog

### Requirement: Do not expose user authoring

The system SHALL NOT expose Definition creation, editing, publishing, cloning,
deletion, validation, preview, or Definition-based execution routes.

#### Scenario: User-authoring route is requested

- **WHEN** a client requests a retired Definition-authoring route
- **THEN** the API responds as an unknown route

### Requirement: Preserve scientific provenance

Definition records SHALL preserve the official algorithm identity without
changing the algorithm's formula, units, quality rules, or result contract.

#### Scenario: Official catalog is loaded

- **WHEN** the official algorithm catalog is requested
- **THEN** every runnable item exposes its Runtime scientific version and
  Definition version
- **AND THEN** no user-authored Definition appears in the catalog
