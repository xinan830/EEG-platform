# analysis/retired-algorithm-removal Specification

## Purpose
Ensure retired algorithm implementations and historical artifact interpretation do not remain maintained execution paths, while current scientific results, definition validation and stored evidence retain explicit ownership.
## Requirements
### Requirement: Historical artifacts do not receive inferred scientific provenance

The application SHALL display an official Full Run window only when its current scientific contract is verifiable. It MUST NOT invent missing preprocessing scope, time-axis semantics or quality evidence. Incompatible artifacts SHALL produce an unavailable state with a stable reason code and a readable instruction to recompute, without a successful numeric result.

#### Scenario: Historical STFT provenance is missing

- **WHEN** a Full Run STFT window lacks required preprocessing or inner-time-axis provenance
- **THEN** the client presents an unavailable result with a structured reason
- **AND** no inferred provenance or current scientific result is displayed

#### Scenario: Current full result is displayed

- **WHEN** a current valid Full Run window is selected
- **THEN** its backend-provided sample range, time axis, units, channel order, quality and provenance are retained without scientific recomputation

### Requirement: Retired calculators are not maintained execution capabilities

The system SHALL exclude retired shadow calculators, realtime metric helpers and compatibility aliases from supported algorithm execution and validation tooling. It SHALL retain current independent numerical references and current full, on-demand and combined capabilities.

#### Scenario: Current algorithm validation runs after cleanup

- **WHEN** the current official algorithm numerical and strategy suites run
- **THEN** they use current references and frozen artifacts without importing retired calculators
- **AND** all declared execution strategies retain their numerical, range, status, quality and provenance contracts

### Requirement: Current definition validation survives retirement cleanup

The system SHALL continue validating current Definition graphs and parameter schemas with stable validation errors. This capability SHALL NOT depend on a retired graph execution or historical calculator compatibility entry point.

#### Scenario: Invalid graph is submitted

- **WHEN** a Definition graph has an unknown node, invalid dependency or cycle
- **THEN** validation rejects it with its documented structured error
- **AND** no graph execution or EEG processing is attempted

### Requirement: Cleanup preserves stored data and unrelated platform capabilities

Retirement cleanup SHALL NOT delete raw recordings, Run metadata, stored artifacts or database structural migrations. Stored identity and checksums SHALL remain unchanged even when a result cannot be rendered under the current scientific contract.

#### Scenario: An incompatible stored result is encountered

- **WHEN** a historical artifact fails current validation
- **THEN** the application reports it as unavailable without rewriting or deleting it
- **AND** current database startup and current recording operations remain operational
