# desktop/on-demand-dynamic-review Specification

## Purpose

Specify desktop consumption of backend-computed on-demand dynamic windows from the review and algorithm detail workspaces.

## ADDED Requirements

### Requirement: Reuse the existing recording cursor

Review and algorithm detail pages SHALL use the existing recording-relative sample cursor and SHALL NOT create a second playback clock. Cursor movement SHALL request or select a backend-defined target window; it SHALL NOT create a full AnalysisRun per playback tick.

#### Scenario: Playback enters a new window

- **WHEN** playback advances to a cursor whose policy-resolved window differs from the displayed window
- **THEN** the desktop requests/observes that window through the shared window coordinator
- **AND** waveform playback remains responsive while the analysis window is queued or computing

#### Scenario: Seek backwards to a ready window

- **WHEN** the user seeks to a previously ensured compatible window
- **THEN** the desktop binds the verified cached/persisted result without recomputing a full dynamic series
- **AND** it does not display a later window as if it belonged to the backward target

### Requirement: Coalesce targets and reject stale responses

The desktop SHALL coalesce repeated cursor updates resolving to the same window, bound foreground requests, and invalidate obsolete responses by recording identity, configuration revision, window identity, and target revision. It SHALL release its subscription/read work on panel close or recording switch without deleting immutable science or cancelling unrelated shared work.

#### Scenario: Rapid seek A to B to C

- **WHEN** requests for A, B, and C overlap and A returns last
- **THEN** only the latest identity-matching target C can replace the visible result
- **AND** obsolete waiters are released without cancelling work still subscribed to by another consumer

### Requirement: Separate target, prior result, and scientific state

While the target window is not ready, the UI SHALL identify the requested target and state of any prior complete chart frame. It SHALL distinguish preparation, queueing, computation, insufficient data, rejected/unavailable result, cancellation, and failure. It SHALL NOT label a previous window as the current target or replace a rejected target with an earlier valid result.

#### Scenario: Target window is still computing

- **WHEN** the user moves to a window that is not ready while a previous complete spectrum is visible
- **THEN** the previous complete frame may remain visible with its actual range clearly marked
- **AND** the target is shown as pending and is not represented by fabricated values

#### Scenario: Target window is rejected

- **WHEN** backend quality rules reject the target window
- **THEN** the UI shows its actual range, quality status, and structured quantitative reason
- **AND** it does not show a stale successful result as the target result

### Requirement: Keep scientific work and display modules separated

The desktop SHALL send normalized scientific configuration and render backend-owned results. Review coordination SHALL remain algorithm-neutral; each algorithm module SHALL own parsing, units, chart series, and result presentation. WPF SHALL NOT reimplement filtering or algorithm formulas.

#### Scenario: PSD response is rendered

- **WHEN** a PSD window response becomes ready and matches the active target/configuration
- **THEN** the PSD presenter binds the returned backend values, axes, units, quality, and provenance
- **AND** the generic review coordinator does not contain PSD arrays or PSD-specific computation

### Requirement: Expose pending and failure status without blocking review

Dynamic analysis preparation, foreground window execution, and result binding SHALL have visible independent status from waveform loading and playback. A structured API or integrity failure SHALL be diagnosable while the existing review waveform and transport remain usable.

#### Scenario: Backend window service is unavailable

- **WHEN** on-demand analysis cannot be queued or read
- **THEN** the UI reports a structured analysis status/error
- **AND** review play, pause, seek, waveform rendering, and lifecycle controls continue to work
