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
Changing the applied PSD channel set or scientific parameters SHALL preserve review position and playback state while creating a new configuration revision. The PSD configuration SHALL capture an immutable ordered analysis channel set and montage identity. Display channel selection and PSD frequency-bin selection SHALL be presentation state over the current result and SHALL NOT create a new Run. Display montage and filtered display samples SHALL remain separate from scientific inputs. The workspace SHALL own shared filter values and analysis window settings; PSD SHALL compute from the configured analysis recording inputs using its declared preprocessing contract.

#### Scenario: Edit shared settings while a Run is being prepared
- **WHEN** shared filters or analysis window length change while old work is pending
- **THEN** old results and progress cannot be presented as the new configuration
- **AND** review position and playback state remain unchanged
- **AND** the user applies the new configuration explicitly

#### Scenario: Select the analysis window from the workspace menu
- **WHEN** the user opens Analysis / Analysis Interval / Analysis Window Length
- **THEN** choices and default come from the backend algorithm policy
- **AND** the PSD panel does not expose duplicate filter or window inputs

#### Scenario: Change channel while paused
- **WHEN** a user changes the PSD spectrum display channel while paused at 17 seconds
- **THEN** the review remains paused at 17 seconds
- **AND** the chart selects that channel from the current channel-resolved Run without submitting another Run
- **AND** unavailable channels are shown with their structured state rather than a substituted channel

#### Scenario: Change the applied PSD channel set
- **WHEN** the user applies a different analysis channel set while paused
- **THEN** the review position and playback state remain unchanged
- **AND** a new configuration revision uses the newly captured ordered set
- **AND** results from the prior configuration cannot replace it

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

### Requirement: Review maps one selected PSD band from the current result
PSD Review SHALL use one channel-resolved PSD result for its spectrum and topomap. Its topomap selector SHALL list exactly Delta (1–4 Hz), Theta (4–8 Hz), Alpha (8–13 Hz), Beta (13–30 Hz), and Gamma (30–50 Hz), and SHALL bind each option to the matching canonical PSD band-mean-density field. For each channel, the backend SHALL integrate the PSD density over the exact band edges and divide by the complete band width. It SHALL retain `V^2/Hz` in the API and report the exact frequency bounds. WPF SHALL convert that value once for display as `µV²/Hz`. The map SHALL use the same current PSD window and bound montage snapshot. Selection SHALL NOT start another PSD Run, reread raw recording samples, calculate values in WPF, or use RBP band-power arrays. Review's shared time coordinator SHALL select the current eligible PSD window and prevent an obsolete response from replacing a newer cursor/window identity.

#### Scenario: Select a canonical band for the current PSD window
- **WHEN** the user selects one of the five canonical PSD bands
- **THEN** the topomap displays the backend-computed per-channel band-mean PSD for exactly that band and current window
- **AND** the response reports the band's exact frequency bounds and `V^2/Hz`
- **AND** the color scale displays `µV²/Hz` after one conversion from `V^2/Hz`
- **AND** the PSD result identity and review playback state remain unchanged

#### Scenario: The selected band is unavailable in the current result
- **WHEN** the bound PSD frequency axis does not cover the selected band's complete range or channel values are unusable
- **THEN** the topomap reports a structured unavailable or invalid-field state
- **AND** it does not clip the range, select another band, or use a previous window

#### Scenario: Playback advances while a PSD topomap is loading
- **WHEN** Review advances to a newer eligible PSD window before the previous topomap response completes
- **THEN** the older response cannot replace the newer window's field
- **AND** the waveform remains controlled by the single Review time coordinator

### Requirement: Run PSD in Review through the shared on-demand channel

The recording-review workspace SHALL expose PSD only when the current PSD catalog entry is runnable and declares on-demand dynamic-window support. PSD SHALL use Review's registered recording, single recording-relative playback cursor, shared analysis binding and backend window coordinator. It SHALL NOT instantiate a retired PSD-specific Review provider, read saved PSD windows through a legacy route, create a full Run per cursor update, or calculate PSD in WPF.

PSD Review SHALL obtain its scientific version, default filter parameters and dynamic window/step values from the current algorithm catalog and configuration contract. It SHALL NOT duplicate those defaults independently in the desktop.

#### Scenario: Open PSD at the current Review cursor

- **WHEN** the user opens PSD from the recording-review workspace
- **THEN** Review sends a current-version dynamic PSD configuration for the selected available EEG source channel and current recording/montage identity
- **AND** the request uses the existing shared recording registration and Review cursor
- **AND** the chart displays only a backend-returned window whose actual range and identity passed the shared validation path.

#### Scenario: PSD catalog capability is unavailable

- **WHEN** PSD is missing, not runnable, or does not declare on-demand windows
- **THEN** PSD preparation reports an explicit unavailable/failure state
- **AND** waveform playback and the other registered algorithms remain usable
- **AND** no legacy PSD route is attempted.

#### Scenario: Render a ready PSD window

- **WHEN** the backend returns a ready PSD window matching the active recording, configuration and target
- **THEN** the PSD-owned presenter validates the `frequency_hz` axis in `Hz`, the `psd` array in `V^2/Hz` or `uV^2/Hz`, and their matching finite shape
- **AND** the Review panel renders the returned spectrum using the PSD module's display projection and chart
- **AND** the tooltip formats frequency and power values as decimals and presents the display unit as `µV²/Hz`
- **AND** when the pointer is within the rendered spectrum's hit tolerance, the hover marker snaps to the nearest returned PSD sample and the tooltip shows that sample's frequency and value
- **AND** when the pointer is outside the rendered spectrum's hit tolerance, the marker and tooltip are hidden
- **AND** the generic Review coordinator remains free of PSD arrays and formulas.

#### Scenario: PSD window is unavailable or rejected

- **WHEN** the backend returns insufficient, rejected, unavailable, cancelled or failed PSD output
- **THEN** the PSD panel shows the backend state/reason and actual range when present
- **AND** it clears the prior spectrum rather than showing stale or fabricated values.

#### Scenario: Change PSD source channel or close its panel

- **WHEN** the user changes the PSD source channel or closes/replaces the panel
- **THEN** the shared Review lifecycle invalidates or releases the matching foreground request/subscription
- **AND** channel changes preserve the Review cursor and playback state
- **AND** late responses from the prior configuration cannot replace the current PSD result.

### Requirement: Review panel identity matches its algorithm execution route

Each exposed Review algorithm panel SHALL bind its own algorithm ID, parameter configuration, result projection, and matching execution adapter. Selecting a panel SHALL submit the selected algorithm ID, retain the backend-declared scientific version and ordered channels, and display only results accepted by the existing recording, configuration and window identity checks. Shared presentation templates SHALL NOT change the selected algorithm, units, absolute sample range, quality state or provenance. A missing or mismatched adapter SHALL fail explicitly rather than route to another algorithm.

#### Scenario: Select an algorithm with a shared scalar template

- **WHEN** the user selects IAPF, Theta/Beta or either BrainBeat algorithm
- **THEN** its own algorithm ID and configuration are sent through its matching adapter
- **AND** the scalar template presents the corresponding registered result projection.

#### Scenario: Select an algorithm with specialized capabilities

- **WHEN** the user selects PSD, RBP or TNK
- **THEN** its specialized adapter and result presentation remain attached to that algorithm
- **AND** shared lifecycle coordination does not replace its scientific calculation route.

#### Scenario: An execution adapter does not match the registered panel

- **WHEN** Review composition supplies an adapter with a different algorithm ID than its panel registration
- **THEN** composition fails with an explicit identity error before an analysis request is made
- **AND** no other algorithm's result is used as a substitute.
