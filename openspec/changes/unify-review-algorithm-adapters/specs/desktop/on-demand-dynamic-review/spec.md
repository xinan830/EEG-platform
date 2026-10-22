## MODIFIED Requirements

### Requirement: Reuse the existing recording cursor

Review and algorithm detail pages SHALL use the existing recording-relative sample cursor and SHALL NOT create a second playback clock. Cursor movement SHALL request or select a backend-defined target window; it SHALL NOT create a full AnalysisRun per playback tick. Playback SHALL request eligible targets according to the active backend policy. During timeline dragging, review SHALL defer target requests until release and then request the final cursor target.

#### Scenario: Playback enters a new window

- **WHEN** playback advances to a cursor whose policy-resolved window differs from the active target
- **THEN** the desktop requests or observes that window through the shared window coordinator
- **AND** waveform playback remains responsive while the analysis window is queued or computing

#### Scenario: Drag releases on a new target

- **WHEN** timeline dragging ends at a cursor whose policy-resolved window differs from the active target
- **THEN** the desktop immediately requests or observes the final target through the shared window coordinator
- **AND** it does not submit requests for the intermediate drag positions

#### Scenario: Seek backwards to a ready window

- **WHEN** the user seeks to a previously ensured compatible window
- **THEN** the desktop binds the verified cached/persisted result without recomputing a full dynamic series
- **AND** it does not display a later window as if it belonged to the backward target

### Requirement: Coalesce targets and reject stale responses
The desktop SHALL coalesce repeated cursor updates resolving to the same
window, bound foreground requests, and invalidate obsolete responses by
recording identity, complete scientific snapshot, configuration revision,
window identity, and target revision. During dragging it SHALL defer analysis
requests and submit the final target at release. It SHALL release its
subscription/read work on panel close or recording switch without deleting
immutable science or cancelling unrelated shared work.

#### Scenario: Rapid seek A to B to C

- **WHEN** requests for A, B, and C overlap and A returns last
- **THEN** only the latest identity-matching target C can replace the visible result
- **AND** obsolete waiters are released without cancelling work still subscribed to by another consumer

#### Scenario: Drag ends while an older request is in flight

- **WHEN** a drag release establishes target C while target A is still pending
- **THEN** A cannot replace C even if its response arrives later
- **AND** cancellation detaches this consumer from A without cancelling work required by other subscribers

#### Scenario: Close analysis during shared work

- **WHEN** the review panel closes while its target is still computing for multiple subscribers
- **THEN** this panel's subscription is released
- **AND** shared backend work continues for other subscribers

### Requirement: Keep scientific work and display modules separated
The desktop SHALL send normalized algorithm-owned scientific configuration
snapshots and render backend-owned results. Review coordination SHALL remain
algorithm-neutral. Each accepted algorithm module SHALL own its request
configuration mapping, result parsing, units, quality/provenance presentation
and chart series. WPF SHALL NOT reimplement filtering, window planning or
algorithm formulas. Display filtering and display montage SHALL not be treated
as analysis inputs unless explicitly declared in that algorithm's contract.

#### Scenario: PSD response is rendered

- **WHEN** a PSD window response becomes ready and matches the active target/configuration
- **THEN** the PSD presenter binds the returned backend values, axes, units, quality, and provenance
- **AND** the generic review coordinator does not contain PSD arrays or PSD-specific computation

#### Scenario: New algorithm is listed but not accepted for review

- **WHEN** an algorithm is present in the catalog but has no accepted review adapter and presenter
- **THEN** the review UI does not expose it as dynamically supported
- **AND** adding the adapter does not require adding its scientific formula to generic review coordination
