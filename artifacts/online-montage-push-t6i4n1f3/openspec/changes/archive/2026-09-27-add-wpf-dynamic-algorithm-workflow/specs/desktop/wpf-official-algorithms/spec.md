# WPF dynamic official algorithms

## ADDED Requirements

### Requirement: Configure declared dynamic modes

The WPF algorithm workflow SHALL expose dynamic mode only for catalog entries
that declare `dynamic`, and SHALL populate window and step choices from the
selected algorithm's `dynamic_policy`.

#### Scenario: Dynamic policy is declared

- **WHEN** a runnable algorithm advertises dynamic mode
- **THEN** the client shows dynamic mode and the backend-provided window and
  step defaults/options.

#### Scenario: Dynamic mode is unavailable

- **WHEN** an algorithm does not advertise dynamic mode
- **THEN** the client keeps the static workflow and does not submit a dynamic
  configuration.

### Requirement: Submit dynamic runs without client science

The WPF client SHALL submit `mode`, `dynamic_window_s`, and `refresh_step_s`
to the existing official algorithm Run endpoint and SHALL NOT calculate EEG
metrics locally.

#### Scenario: Submit a dynamic run

- **WHEN** the user selects dynamic mode and submits a valid range
- **THEN** the client sends the selected channel, range, mode, window, and step
  to the existing official algorithm endpoint.

### Requirement: Render dynamic result provenance

The client SHALL display backend-returned time points or structured window
states, units, and quality summaries. `Partial`, `Complete`, `Rejected`, and
`Unavailable` SHALL remain distinguishable, and null values SHALL NOT be shown
as zero.

#### Scenario: Display dynamic states

- **WHEN** a completed dynamic Run contains multiple window states
- **THEN** the client displays the backend window count, state counts, and unit
  without changing the returned values.

### Requirement: Preserve static workflows

Adding dynamic controls SHALL NOT change static request payloads or existing
static result rendering.

#### Scenario: Run an existing static algorithm

- **WHEN** the user keeps static mode selected
- **THEN** the client sends the existing static payload and renders the existing
  scalar or structured result path.

### Requirement: Preserve dynamic RBP bands

Dynamic RBP SHALL return Delta, Theta, Alpha, and Beta relative-power values
for every planned window. The client SHALL display those backend values without
reducing them to one scalar or recomputing them.

#### Scenario: Dynamic RBP window

- **WHEN** a dynamic RBP Run completes for a valid window
- **THEN** the result contains all four band values and the window state.
