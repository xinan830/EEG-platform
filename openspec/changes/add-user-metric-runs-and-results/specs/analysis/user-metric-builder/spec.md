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

#### Scenario: Enter a static time range directly

- **WHEN** a user enters `12.000 s` as start and `42.000 s` as end
- **THEN** the static run uses exactly `12.000–42.000 s`
- **AND THEN** the user may instead fill those fields from the current committed
  analysis range.

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

The system SHALL support a persistent dynamic playback mode with a selectable
real trailing spectral window of `5`, `10`, `20`, or `30` seconds and a fixed
one-second step. Starting when playback reaches the selected window duration,
it SHALL evaluate saved definitions against that real trailing EEG window for
each whole playback second. Each returned point SHALL retain its window start,
window end, output value or null, and quality state. The selected duration
SHALL be persisted in the Run configuration and returned dynamic contract.

#### Scenario: Dynamic Theta/Beta trend

- **WHEN** a user selects a `20 s` dynamic window and playback reaches `46.x s`
- **THEN** the client requests the backend result for `26–46 s`
- **AND WHEN** playback reaches `47.x s`
- **THEN** it requests `27–47 s` and appends that returned point to the trend
- **AND THEN** every point represents a real selected-duration window
- **AND THEN** a quality-gated point remains present with null value.

#### Scenario: Enable dynamic mode in the middle of playback

- **WHEN** a user enables a `10 s` dynamic metric at `25 s`
- **THEN** the browser requests a bounded real-history range that returns
  the `10…25 s` one-second endpoints
- **AND THEN** the chart connects only backend-returned points
- **AND THEN** it does not interpolate missing metric values in the browser.

#### Scenario: Retain trend while an appended Run is pending

- **WHEN** a dynamic append Run is `queued` or `running` and its response has
  no metric result yet
- **THEN** the client keeps the previously rendered backend-derived trend
- **AND WHEN** the appended Run completes with a dynamic payload
- **THEN** the client merges its real points by `window_end_s` without
  duplicating or removing earlier points.

#### Scenario: Change the active dynamic window

- **WHEN** a user changes an active dynamic metric from `10 s` to `20 s`
- **THEN** the prior trend is cleared and a new real-history bootstrap uses the
  `20 s` contract
- **AND THEN** the visible X-axis covers the most recent `20 s` ending at the
  newest returned endpoint, rather than a range derived from the count of
  available points.

#### Scenario: Replay a dynamic metric session

- **WHEN** the user replays a recording after a dynamic metric produced a point
  at `22 s`
- **THEN** the client clears those prior-epoch metric points immediately
- **AND THEN** it retains the selected definitions, channel and dynamic-window
  setting
- **AND THEN** at `0 s` it displays an empty pending chart for the first real
  `0–window_s` interval
- **AND WHEN** playback reaches `window_s`
- **THEN** it requests and displays the first real dynamic point.

### Requirement: Present read-only algorithm computation evidence

The system SHALL provide a read-only algorithm debug workbench from each
displayed static metric card and dynamic metric trend. It SHALL render only
persisted backend Run data: algorithm/run identity, versions, configuration
fingerprint, requested and actual ranges, selected channel, reference,
sampling rate, filter and Welch contract, quality evidence, resolved inputs,
and computed output. It SHALL make the selected channel's frequency axis,
linear PSD, absolute band power and relative band power available for
inspection without browser-side scientific recalculation.

#### Scenario: Inspect a dynamic Theta/Beta point

- **WHEN** a researcher opens the workbench from a Theta/Beta trend and chooses
  endpoint `22 s`
- **THEN** it shows the persisted real trailing window, such as `12–22 s` for
  a `10 s` contract
- **AND THEN** it shows the persisted Theta and Beta inputs, ratio output,
  quality and spectral evidence for that exact window
- **AND THEN** it never substitutes values from another endpoint or calculates
  a new value in the browser.

### Requirement: Keep algorithm execution outside the definition library

The ordinary definition library SHALL provide explanation, version information
and deletion only. The waveform-and-algorithms workspace SHALL provide saved
algorithm selection, static/dynamic execution, and measured result display.

#### Scenario: Open the algorithm library

- **WHEN** a user opens the algorithm library
- **THEN** the page shows definitions, formulas, versions and deletion actions
- **AND THEN** it does not show EEG run controls or measured result cards.

### Requirement: Allow a local user to delete a private algorithm after use

The system SHALL allow a local user to delete a private algorithm definition
and its versions even when completed analysis runs or batch records reference
it. Those historical records SHALL retain their already persisted values,
provenance and spectral evidence as read-only data. The deletion confirmation
SHALL state that the historical record cannot subsequently reopen the deleted
definition. Queued work referencing the definition SHALL be cancelled, and
running work SHALL receive a cancellation request before the definition is
removed. Platform-official definitions SHALL remain protected.

#### Scenario: Delete a private algorithm with historical results

- **WHEN** a user deletes a private Theta/Beta definition that has completed
  analysis results
- **THEN** the definition no longer appears in the library or run selector
- **AND THEN** the existing result records remain readable with their saved
  evidence
- **AND THEN** a queued run using that definition is cancelled rather than
  executing a now-deleted algorithm.
