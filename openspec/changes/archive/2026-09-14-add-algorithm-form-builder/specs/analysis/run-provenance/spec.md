## MODIFIED Requirements

### Requirement: Persist analysis run lifecycle

The system SHALL assign every new run an immutable identifier and persist
exactly one lifecycle status from `queued`, `running`, `completed`,
`gate_failed`, `failed`, or `cancelled` together with creation and update
timestamps. A preview run SHALL use the same lifecycle and set
`is_preview=true`; it SHALL remain isolated from formal result paths.

#### Scenario: Complete a synchronous run

- **WHEN** a compatible analysis finishes successfully
- **THEN** the run transitions through persisted lifecycle state to `completed` and exposes its result summary

#### Scenario: Complete a preview run

- **WHEN** a validated scalar definition preview finishes successfully
- **THEN** it persists `is_preview=true`, a preview-only result summary, and does not overwrite a formal run or artifact

#### Scenario: Reject by quality gate

- **WHEN** scientific quality rules reject the available input
- **THEN** the run ends as `gate_failed`, retains structured quality reasons, and does not report rejected numerical output as zero
