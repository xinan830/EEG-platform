## ADDED Requirements

### Requirement: Run a saved scalar metric against one static EEG range

The system SHALL allow a private saved metric definition version to be run
against one recording, one existing channel, and one static range of at least
four seconds. The backend SHALL resolve its curated absolute or relative band
power inputs from the frozen offline-spectral-v3 analysis path and execute the
persisted definition graph.

#### Scenario: Run a saved Theta/Beta formula

- **WHEN** a user selects a saved Theta-power divided by Beta-power definition,
  a recording channel, and a valid static time range
- **THEN** the server creates a traceable `definition_metric` AnalysisRun
- **AND THEN** a completed result contains the executor output, `dimensionless`
  unit, input snapshot, channel, actual range, and quality state.

### Requirement: Reject unavailable metric input without fabricating output

The backend SHALL reject a missing channel, unsupported feature identifier,
unknown definition version, or quality-gated spectrum with a structured run
failure. A rejected metric output SHALL be null and SHALL never be recorded as
zero.

#### Scenario: Quality gate rejects source spectrum

- **WHEN** the source analysis does not meet the spectral quality gate
- **THEN** the metric run becomes `gate_failed`
- **AND THEN** its result records the quality reason and has no numeric metric
  output.

### Requirement: Present scalar results without misleading chart axes

The ordinary UI SHALL show a completed scalar metric as a value card with its
persisted unit, channel, actual range, and quality state. It SHALL only show
an input comparison chart when the displayed input values share the same
persisted unit; the Y axis SHALL use that unit and the X axis SHALL name the
inputs.

#### Scenario: Display a completed Theta/Beta result

- **WHEN** a metric run resolves Theta and Beta power in `uV^2` and produces a
  ratio output
- **THEN** the UI presents the ratio as the primary value and MAY present a
  two-bar Theta/Beta input comparison labelled `uV^2`
- **AND THEN** it does not present the ratio as a fabricated one-point line
  chart.

### Requirement: Future trend axes remain unit-safe

When a future metric mode produces multiple ordered windows, the UI SHALL use
time in seconds as the X axis and the result's persisted output unit as the Y
axis. The user SHALL NOT assign arbitrary physical units to an axis.

#### Scenario: Display a future metric trend

- **WHEN** a future run returns multiple metric windows
- **THEN** the trend uses `时间（s）` and the backend-declared output unit
- **AND THEN** a browser-side unit override is unavailable.
