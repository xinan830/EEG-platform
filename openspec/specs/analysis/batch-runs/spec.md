# analysis/batch-runs Specification

## Purpose
TBD - created by archiving change add-projects-and-batch-runs. Update Purpose after archive.
## Requirements
### Requirement: Freeze and expand a BatchRun

The system SHALL persist a BatchRun with a Project identity, fixed definition
identity/version, and immutable request snapshot. It SHALL expand only Project
recordings into deterministic idempotent child AnalysisRuns.

#### Scenario: Create a batch for project recordings
- **WHEN** a valid BatchRun request references sessions from one Project
- **THEN** the system creates child queued Runs in recording order and preserves
  their BatchRun identity

### Requirement: Report per-recording terminal outcomes

The system SHALL report batch children separately as `completed`, `gate_failed`,
`missing_channel`, `insufficient_duration`, `failed`, or `cancelled`.

#### Scenario: Preflight a missing requested channel
- **WHEN** a batch request selects a channel absent from a Project recording
- **THEN** that child is recorded as `missing_channel` with null output and
  other eligible children remain queued
