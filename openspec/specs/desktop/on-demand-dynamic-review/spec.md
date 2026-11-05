# desktop/on-demand-dynamic-review Specification

## Purpose
Specify desktop consumption of backend-computed on-demand dynamic windows from the review and algorithm detail workspaces.
## Requirements
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

### Requirement: PSD and RBP Review use a ten-second default analysis window
The data Review PSD and RBP panels SHALL default to a 10-second dynamic analysis window and a 1-second refresh step, as declared by each algorithm's official catalog policy. This outer analysis window SHALL remain distinct from the four-second Welch segment with 50% overlap; this requirement does not alter Welch computation parameters.

#### Scenario: Open the PSD Review panel with catalog defaults
- **WHEN** the user opens the data Review PSD panel without changing its dynamic policy values
- **THEN** the desktop request uses a 10-second dynamic analysis window and a 1-second refresh step
- **AND** the PSD calculation retains its declared four-second Welch segment and 50% overlap

#### Scenario: Open the RBP Review panel with catalog defaults
- **WHEN** the user opens the data Review RBP panel without changing its dynamic policy values
- **THEN** the desktop request uses a 10-second dynamic analysis window and a 1-second refresh step
- **AND** the RBP calculation retains its declared four-second Welch segment and 50% overlap

### Requirement: Review selects and submits the Theta/Beta frequency mode
The recording Review Theta/Beta panel SHALL expose Standard and IAF-relative modes, default to Standard, and submit the selected mode with each on-demand scientific configuration. The panel SHALL display backend-owned results and SHALL NOT calculate frequency bands or the ratio in WPF. Its dynamic policy SHALL use the catalog-declared 20-second default window and one-second refresh step without numeric warm-up values.

#### Scenario: Change mode and recompute the current Review window
- **WHEN** the user changes the Theta/Beta mode while the Review panel is open
- **THEN** the active configuration is invalidated and the current cursor window is recomputed with the selected mode
- **AND** the request carries that mode to the backend

#### Scenario: Wait for a complete Review window
- **WHEN** the playback cursor is before the first complete 20-second TBR window
- **THEN** Review shows the window as insufficient or pending
- **AND** it does not display a partial numeric ratio

### Requirement: Show the actual analysis range on PSD and RBP Review visualizations
The data Review PSD spectrum and topomap, and the RBP history charts and topomap, SHALL show the current visualization's actual analysis start and end times in seconds at the lower-left. The displayed range SHALL come from the corresponding backend result response. If that response does not provide a valid range, the visualization SHALL show an unavailable state rather than infer a range from the playback cursor or display history, and it SHALL not show a stale successful result as the target result.

#### Scenario: Display a PSD spectrum range
- **WHEN** a PSD dynamic-window response with an actual start and end is rendered as a spectrum
- **THEN** the PSD spectrum shows that returned range in seconds at its lower-left

#### Scenario: Display a PSD topomap range
- **WHEN** a PSD topomap response with an actual start and end is rendered
- **THEN** the PSD topomap shows the topomap response's returned range in seconds at its lower-left

#### Scenario: Display RBP ranges
- **WHEN** an RBP dynamic-window response is rendered as history charts or a topomap
- **THEN** each RBP history chart and the topomap view show the corresponding current result range in seconds at the lower-left

#### Scenario: Backend range is missing
- **WHEN** a PSD or RBP visualization has no valid backend-returned analysis range
- **THEN** it shows that the range is unavailable
- **AND** it does not derive a substitute from the cursor or visible history
- **AND** it does not show a stale successful result as the target result

### Requirement: Review identifies the selected BrainBeat variant

The recording-review workspace SHALL allow users to select classic BrainBeat
or the individualized IAPF-relative variant. It SHALL submit the selected
official algorithm identity and catalog-declared dynamic window settings to
the backend. The desktop SHALL display the returned variant label, actual
analysis range, ratio unit, quality state, and failure reason without
recomputing either formula.

#### Scenario: Select classic BrainBeat in review
- **WHEN** the user selects classic BrainBeat and requests the current cursor window
- **THEN** the request identifies `brainbeat` and uses the catalog-declared 20-second/one-second policy
- **AND** the result presentation identifies fixed-band absolute-power semantics

#### Scenario: Select individualized BrainBeat in review
- **WHEN** the user selects the individualized variant and requests the current cursor window
- **THEN** the request identifies `brainbeat_individualized` and uses its catalog-declared policy
- **AND** the result presentation identifies IAPF-relative semantics

#### Scenario: Show an unavailable BrainBeat window
- **WHEN** the selected BrainBeat result is insufficient, rejected, or unavailable
- **THEN** review shows the backend state and actual requested range
- **AND** it does not show a stale or fabricated numeric result

