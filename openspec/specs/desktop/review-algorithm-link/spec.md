# desktop/review-algorithm-link Specification

## Purpose
TBD - created by archiving change link-offline-review-psd. Update Purpose after archive.
## Requirements
### Requirement: Review owns the only playback cursor
Offline analysis panels SHALL consume the recording-relative review cursor.
They SHALL NOT run an independent playback clock or use viewport boundaries
as the scientific cursor. Playback and seeking SHALL NOT create scientific Runs.

#### Scenario: Seek a completed result
- **WHEN** the user seeks an enabled PSD panel to 17.4 seconds
- **THEN** the panel selects the latest backend window ending no later than the cursor
- **AND** it displays the actual window range without submitting another Run.

### Requirement: Resolve windows using recording-relative sample boundaries
Window selection SHALL use int64 recording-relative sample coordinates and
half-open saved ranges. Gaps SHALL retain their timeline positions. Seconds
SHALL be converted consistently using floor and SHALL NOT be rounded forward.
The resolver SHALL select one uniquely indexed latest window whose exclusive
end is no later than the cursor, not the nearest window centre.

#### Scenario: Cursor crosses an overlapping window endpoint
- **WHEN** a 1000 Hz recording has a saved window ending at sample 4000
- **THEN** cursor 3.999999 seconds cannot select that window
- **AND** cursors 4 and 4.000001 seconds can select it if it is the latest eligible window.

#### Scenario: The recording ends between planned steps
- **WHEN** a recording ends after the last saved endpoint but before the next step
- **THEN** review displays the last saved range and identifies its coverage end
- **AND** it does not fabricate a trailing Partial window.

### Requirement: Keep offline and causal analysis distinct
The first linked PSD panel SHALL retain the existing offline scientific contract
and expose preprocessing phase. Window selection SHALL NOT be described as
proof that zero-phase preprocessing has no future-sample dependency.

#### Scenario: Replay a zero-phase PSD Run
- **WHEN** review presents a saved offline PSD window
- **THEN** provenance identifies offline zero-phase processing
- **AND** the panel does not claim real-time causal equivalence.

#### Scenario: Read one saved window
- **WHEN** the panel requests one PSD result window
- **THEN** reading does not re-filter a short EEG slice or change saved preprocessing context.

### Requirement: Ensure compatible Runs without permanent request-key reuse
An analysis preparation operation SHALL reuse a compatible completed Run only
when its result integrity is valid, observe compatible queued/running work, or
create new work when no eligible Run exists. Execution failure or cancellation
SHALL permit explicit retry with a new request identity. Scientific fingerprints
SHALL exclude display montage, cursor, playback rate and display-only processing
state. Shared high-pass, low-pass and notch values SHALL be captured as scientific
configuration when the user applies analysis, and included in its fingerprint.

#### Scenario: Reopen the same configured panel
- **WHEN** the same recording and scientific configuration have a verified completed Run
- **THEN** the panel reuses that Run within the declared reuse scope
- **AND** it preserves the original Run provenance rather than recomputing.

#### Scenario: Retry a cancelled configuration
- **WHEN** the user explicitly retries cancelled work with unchanged scientific configuration
- **THEN** a permanent idempotency key does not force reuse of the cancelled Run
- **AND** the operation remains constrained by project and shared-task permissions.

#### Scenario: A completed window is scientifically rejected
- **WHEN** a matching completed Run contains a Rejected window
- **THEN** that saved quality state can be reused as unavailable science
- **AND** repeated automatic recomputation is not used to conceal the rejection.

### Requirement: Scrubbing retains bounded continuous feedback
Review SHALL preserve throttled loading during dragging, coalesce obsolete
targets and deduplicate cached/in-flight result reads. It SHALL NOT require
mouse release for all updates or submit a result request for every render tick.

#### Scenario: Repeated cursor updates resolve to a cached window
- **WHEN** many drag or playback updates resolve to the same cached window
- **THEN** no additional window HTTP read or scientific Run is created.

### Requirement: Preserve review time across scientific configuration changes
Changing the analysis source channel or scientific parameters SHALL preserve
review position and playback state while creating a new configuration revision.
Display montage and filtered display samples SHALL remain separate from scientific
inputs. The workspace SHALL own shared filter values and the analysis window setting;
PSD SHALL consume an immutable snapshot and compute from the raw recording.

#### Scenario: Edit shared settings while a Run is being prepared
- **WHEN** shared filters or analysis window length change while old work is pending
- **THEN** old results and progress cannot be presented as the new configuration
- **AND** review position and playback state remain unchanged
- **AND** the user applies the new configuration explicitly.

#### Scenario: Select the analysis window from the workspace menu
- **WHEN** the user opens Analysis / Analysis Interval / Analysis Window Length
- **THEN** choices and default come from the backend algorithm policy
- **AND** the PSD panel does not expose duplicate filter or window inputs.

#### Scenario: Change channel while paused
- **WHEN** a user changes the PSD source channel while paused at 17 seconds
- **THEN** the review remains paused at 17 seconds
- **AND** a compatible cached Run or new Run is used for that channel
- **AND** obsolete results cannot replace the new configuration.

### Requirement: Present unavailable windows without stale substitution
The panel SHALL distinguish requested cursor, presented waveform coverage and
actual analysis window. An unavailable or rejected latest window SHALL NOT be
replaced by an earlier valid result as though it were current.

#### Scenario: Current window is rejected
- **WHEN** the backend-selected window is rejected
- **THEN** the panel displays its state, actual range and supplied quality evidence
- **AND** no fabricated zero or previous valid spectrum represents that window.

### Requirement: Module ownership is independent from review composition
Review SHALL coordinate algorithm-neutral lifecycle and cursor state. PSD-owned
presentation SHALL retain its own values, units and chart. Review SHALL NOT
instantiate the entire catalog workspace or implement scientific calculations.

#### Scenario: Close the analysis panel
- **WHEN** an enabled panel is closed
- **THEN** its subscriptions and foreground reads are released
- **AND** waveform review remains operational
- **AND** immutable Runs and shared backend work are not blindly deleted or cancelled.

### Requirement: Review exposes accepted algorithm panels through the shared lifecycle
Review SHALL expose only algorithm panels whose current catalog entry and adapter support the declared on-demand contract. It SHALL keep one active panel, one registered recording identity, and one playback time coordinator. Each algorithm panel SHALL own its request configuration and result projection; Review SHALL NOT calculate scientific metrics or instantiate the standalone algorithm workspace.

TNK SHALL use the existing ordered playback-event contract. Its five required source roles SHALL be explicitly mapped to distinct channels. Every canonical event required for state continuity SHALL be consumed in sequence, including windows earlier than the panel-open cursor. The visible chart history SHALL begin at the cursor captured when TNK opens and SHALL preserve actual backend window times, units, quality, unavailable values, and gaps.

Review SHALL retain every accepted TNK result window for the active delivery
session, without a fixed window-count limit. The panel-open cursor filters the
visible projection only; it SHALL NOT evict earlier accepted windows from the
session history. Session reset or panel close clears that history.

The Review TNK chart SHALL not render an algorithm-owned playback cursor or
expand its time axis to the Review playback position. Its visible right edge
SHALL follow the latest accepted result window, while the waveform remains the
single visible owner of the shared playback cursor. The Review summary may use
the shared cursor to select the current result and report analysis lag.

When TNK is active, Review SHALL dock its algorithm panel below the waveform preview. Inside that panel, Review SHALL place the selected group trend in the left algorithm column and show the composite meditation score, state-duration distribution, and recent valid metrics in a right-side summary alongside the five role-channel selectors. All nine metric-group selectors SHALL appear in one row above the trend chart and remain within the left algorithm column. Other algorithm panels SHALL retain the existing side-by-side dock. The chart, recent-valid metrics, and state-duration summary SHALL exclude results before panel open; prior windows may only seed backend state and SHALL not contribute to visible history or panel-open-relative duration totals.

#### Scenario: Open TNK after playback has advanced
- **WHEN** the user opens TNK at recording time 30 seconds
- **THEN** the panel captures 30 seconds as its visible chart start
- **AND** the backend may replay earlier canonical windows to establish TNK state
- **AND** the chart shows no points earlier than 30 seconds.

#### Scenario: TNK summary and metric navigation are shown in Review
- **WHEN** the TNK panel is open
- **THEN** the selected metric group is plotted in the main area and the composite score summary with role-channel selectors appears at the right
- **AND** the nine metric groups are selectable from one row above the chart within the left column
- **AND** the score-state durations and recent-valid values include only results at or after the panel-open cursor.

#### Scenario: TNK roles are missing or duplicated
- **WHEN** one of `Fz`, `Pz`, `Oz`, `F3`, or `F4` is missing or two roles use the same source channel
- **THEN** Review reports the role-mapping error
- **AND** it does not submit the TNK calculation using a guessed or proxy channel.

#### Scenario: TNK events arrive during forward playback
- **WHEN** ordered events arrive while the Review cursor advances
- **THEN** the panel deduplicates them by delivery sequence and scientific window identity
- **AND** it plots each result at the backend's actual recording-relative window time
- **AND** the waveform clock does not wait for event polling or projection.

#### Scenario: TNK playback exceeds 600 accepted windows

- **WHEN** the active delivery session accepts more than 600 canonical windows
- **THEN** the earliest accepted result remains in session history
- **AND** the chart continues to show all accepted windows at or after panel open.

#### Scenario: Review playback is ahead of TNK results

- **WHEN** the Review playback cursor advances beyond the latest accepted TNK window
- **THEN** the TNK chart keeps its right edge at the latest accepted result
- **AND** it shows no algorithm-owned cursor or blank future region
- **AND** the waveform cursor remains visible and continues to follow Review playback.

#### Scenario: TNK playback seeks backward
- **WHEN** the Review cursor moves backward while TNK is active
- **THEN** the adapter closes the old delivery generation and creates a fresh ordered replay context
- **AND** late events from the old generation cannot alter the active chart.

#### Scenario: TNK panel closes or is replaced
- **WHEN** the user closes TNK or opens another algorithm panel
- **THEN** TNK event polling and target observation are cancelled and its delivery session is closed
- **AND** Review waveform playback and its time coordinator continue unchanged.

#### Scenario: TNK delivery has a gap or unavailable child
- **WHEN** the backend reports a delivery discontinuity or a child result is unavailable
- **THEN** the panel displays the affected range and structured state/reason
- **AND** it does not fill the gap with zero or claim complete coverage.
