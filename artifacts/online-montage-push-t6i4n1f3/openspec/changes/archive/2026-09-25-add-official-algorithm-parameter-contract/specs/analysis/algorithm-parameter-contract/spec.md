# analysis/algorithm-parameter-contract Specification

## ADDED Requirements

### Requirement: Declare backend-owned parameter constraints

Every official algorithm parameter schema SHALL be able to declare its value
type, enum options, inclusive numeric bounds, and numeric step metadata. The
existing algorithm catalog SHALL return these fields unchanged.

#### Scenario: Read an official catalog

- **WHEN** a client reads an official algorithm entry
- **THEN** numeric parameters include their applicable bounds and step
  metadata, and enum parameters include their allowed options

### Requirement: Validate parameters before execution

The shared runtime SHALL validate a submitted typed configuration against the
selected module's parameter schema before loading EEG data or executing an
algorithm.

#### Scenario: Reject an out-of-range numeric parameter

- **WHEN** a submitted numeric value is below its minimum or above its maximum
- **THEN** execution SHALL not start and SHALL return a validation error

#### Scenario: Reject an unknown enum value

- **WHEN** a submitted enum value is not declared by the selected module
- **THEN** execution SHALL not start and SHALL return a validation error

### Requirement: Persist validated local parameter presets

The system SHALL allow a local user to create, list, update, and delete a
named preset for one exact algorithm scientific version. A preset SHALL be
accepted only after the selected runtime module validates its configuration.

#### Scenario: Save a valid preset

- **WHEN** a user saves a named configuration for a registered algorithm
- **THEN** the backend stores the algorithm ID, scientific version, complete
  configuration, and configuration hash

#### Scenario: Reject an invalid preset

- **WHEN** a preset contains an invalid range, enum, or numeric value
- **THEN** the backend returns a structured validation error and stores no
  preset

#### Scenario: Preserve Run provenance

- **WHEN** a user starts a Run from a preset
- **THEN** the Run stores its own copied configuration and version, and remains
  readable if the preset is later edited or deleted
