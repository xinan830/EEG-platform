# analysis/extension-governance Specification

## Purpose
TBD - created by archiving change define-extension-and-governance-boundaries. Update Purpose after archive.
## Requirements
### Requirement: Validate non-executable local module manifests

The system SHALL validate a typed module manifest with source,
platform-compatibility, validation state, declared inputs/outputs, declarative
permissions, and a non-arbitrary execution mode. It SHALL support only
`local_official`, `research_template`, and `user_private` sources.

#### Scenario: Inspect a research template manifest
- **WHEN** a valid `research_template` manifest declares approved scientific
  value types and `closed_definition_graph` execution
- **THEN** the system SHALL return its normalized metadata and state that the
  manifest does not grant runtime permissions.

### Requirement: Reject executable or privileged extension declarations

The system SHALL reject unknown fields, unknown scientific types, unsupported
permissions, arbitrary code execution and non-local extension declarations.

#### Scenario: Request Python execution
- **WHEN** a manifest requests a Python, process, network, filesystem or
  otherwise unapproved execution capability
- **THEN** validation SHALL fail before any code is loaded or executed.

### Requirement: Preserve local-workstation governance boundaries

The system SHALL document that module manifests are not marketplace plugins,
signature verification, multi-tenant authorization or a sandbox. Any future
Python plugin execution SHALL require a separate security change with isolated
processes, read-only inputs, temporary directories, no network, resource
limits and dependency allowlists.

#### Scenario: Propose a future Python plugin
- **WHEN** a future feature needs third-party Python execution
- **THEN** it SHALL require a separate approved security change before any
  executable plugin capability is added to this local workstation.

