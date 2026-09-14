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

### Requirement: Run saved metrics across dynamic EEG windows

The system SHALL support a dynamic metric mode over a committed outer range of
at least 10 seconds. It SHALL evaluate the saved definition against trailing
10-second spectral windows at 1-second steps. Each returned point SHALL retain
its window start, window end, output value or null, and quality state.

#### Scenario: Dynamic Theta/Beta trend

- **WHEN** a user runs Theta/Beta over `10-40 s` in dynamic mode
- **THEN** the backend returns 21 points with `window_end_s` from `20` through
  `40` seconds
- **AND THEN** every point represents a real 10-second window
- **AND THEN** a quality-gated point remains present with null value.

### Requirement: Keep algorithm execution outside the definition library

The ordinary definition library SHALL provide explanation, version information
and deletion only. The waveform-and-algorithms workspace SHALL provide saved
algorithm selection, static/dynamic execution, and measured result display.

#### Scenario: Open the algorithm library

- **WHEN** a user opens the algorithm library
- **THEN** the page shows definitions, formulas, versions and deletion actions
- **AND THEN** it does not show EEG run controls or measured result cards.
