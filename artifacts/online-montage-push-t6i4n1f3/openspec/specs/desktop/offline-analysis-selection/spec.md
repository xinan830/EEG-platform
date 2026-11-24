# desktop/offline-analysis-selection Specification

## Purpose
TBD - created by archiving change enable-accepted-offline-analysis-selection. Update Purpose after archive.
## Requirements
### Requirement: Select accepted offline strategies without startup commands

Accepted algorithm modules SHALL declare their default on-demand availability locally. The dynamic parameter card SHALL offer full and on-demand analysis for such modules without requiring environment variables. Full analysis SHALL remain the initial choice. Modules without explicit default-on approval SHALL remain default-off, and modules without window capability SHALL NOT be enabled by a rollout setting alone.

#### Scenario: Start without rollout variables

- **WHEN** the user starts the current source and selects any accepted dynamic algorithm without configuring rollout variables
- **THEN** the analysis strategy dropdown offers full and on-demand analysis
- **AND** full analysis is selected until the user chooses otherwise.

#### Scenario: Apply a maintenance override

- **WHEN** the corresponding backend and WPF per-algorithm variables are explicitly set to `0`
- **THEN** on-demand capability is disabled for that algorithm
- **AND** the WPF dropdown offers only full analysis and uses the full Run API.

#### Scenario: Add an unapproved module

- **WHEN** a module has not explicitly declared default-on approval
- **THEN** an absent rollout variable does not enable its on-demand path.

### Requirement: Keep strategy selection separate from science

The existing strategy dropdown SHALL bind bidirectionally to the execution choice and SHALL clear obsolete result projections when changed. It SHALL NOT modify recording, channel sources, filters, scientific parameters, windows, units, quality gates, cache identity or algorithm formulas. The source launcher SHALL NOT force an algorithm rollout setting that overrides module defaults or explicit maintenance settings.

#### Scenario: Switch from on-demand to full

- **WHEN** the user changes the dropdown from on-demand to full analysis
- **THEN** the page releases its on-demand observation and clears its obsolete projection
- **AND** subsequent execution uses the full Run path with the same scientific inputs.

