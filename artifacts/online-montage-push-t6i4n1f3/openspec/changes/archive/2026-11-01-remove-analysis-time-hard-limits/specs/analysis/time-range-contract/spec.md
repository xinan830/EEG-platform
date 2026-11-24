## ADDED Requirements

### Requirement: Analysis ranges have no fixed global duration cap

Static spectrum and spectrogram requests SHALL accept an analysis duration of
at least four seconds without imposing a fixed global maximum duration. The
requested interval SHALL remain within the recording duration, subject only to
the documented sample-boundary tolerance.

#### Scenario: Long in-bounds request

- **WHEN** a client requests a 149.982-second spectrum or spectrogram interval
  from a recording that contains that interval
- **THEN** the endpoint accepts the request

#### Scenario: Request exceeds recording end

- **WHEN** a client requests an interval whose end exceeds the recording duration
- **THEN** the service returns a structured validation error
- **AND THEN** it does not silently shorten the interval

### Requirement: Dynamic-window policy is separate from analysis duration

Each official algorithm SHALL expose and validate its supported dynamic-window
values through its manifest `dynamic_policy`. A generic 120-second upper bound
SHALL NOT be advertised as a dynamic-window constraint.

#### Scenario: Algorithm-owned dynamic-window options

- **WHEN** a client reads a runnable official algorithm's parameter schema and
  dynamic policy
- **THEN** the schema does not report a generic 120-second maximum for
  `window_s`
- **AND THEN** the manifest reports the values that algorithm supports

### Requirement: Official Run polling has no fixed duration cutoff

The official algorithm workspace SHALL continue polling a queued or running
Run until the backend reports a terminal state. It SHALL NOT stop after a
fixed number of client polling attempts.

#### Scenario: Long-running queued Run

- **WHEN** an official Run remains queued or running beyond the old 30-second
  polling period
- **THEN** the workspace continues polling
- **AND THEN** it renders the terminal result or structured failure when the
  backend returns one
