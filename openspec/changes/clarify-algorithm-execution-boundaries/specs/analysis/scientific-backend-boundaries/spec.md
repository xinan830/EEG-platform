## ADDED Requirements

### Requirement: Keep generic result serialization independent of algorithm identity

The generic execution layer SHALL serialize only declared result contracts and common evidence. It SHALL NOT branch on a concrete algorithm ID to infer scientific meaning, output structure, units, channel roles, quality, or provenance. Algorithm modules SHALL declare their output kind, array/axis units, algorithm-specific evidence, and role ordering through typed contracts; common validation and artifact storage SHALL remain shared.

#### Scenario: Serialize an algorithm-specific structured result
- **WHEN** a module returns a typed structured result with declared axes, arrays, units, quality, and evidence
- **THEN** the common serializer validates that contract and stores large arrays as immutable artifacts
- **AND** it does not infer array semantics from the algorithm ID

#### Scenario: Add a new official algorithm result kind
- **WHEN** a new algorithm uses an already-supported result contract
- **THEN** registration and module-owned declarations are sufficient to describe its scientific output
- **AND** no algorithm-specific branch is added to generic Run lifecycle or serialization services

#### Scenario: Return malformed module output
- **WHEN** output arrays, axes, units, role ordering, or evidence violate their declared contract
- **THEN** the runtime rejects the result with a structured error before publishing it as completed
