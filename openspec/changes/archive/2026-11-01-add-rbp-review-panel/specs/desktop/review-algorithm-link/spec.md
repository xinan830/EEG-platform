## MODIFIED Requirements

### Requirement: Review owns the only playback cursor
Offline analysis panels SHALL consume the recording-relative review cursor.
They SHALL NOT run an independent playback clock or use viewport boundaries
as the scientific cursor. Playback and seeking SHALL NOT create scientific Runs
per cursor update. A registered RBP panel SHALL use the same cursor sample,
recording identity, sampling rate and playback generation as Review; it SHALL
not create a second recording session or time coordinator.

#### Scenario: Seek an RBP panel to a new cursor
- **WHEN** the user seeks the enabled RBP panel to 17.4 seconds
- **THEN** the panel submits the recording-relative cursor to the backend window path
- **AND** it displays the backend-selected actual window range
- **AND** it does not create a second playback clock or a scientific Run for the seek.

#### Scenario: Close the RBP panel while playback continues
- **WHEN** the user closes the RBP panel during waveform playback
- **THEN** RBP foreground reads and subscriptions are released
- **AND** the Review cursor and waveform playback continue with the same time coordinator.

#### Scenario: Seek a completed result
- **WHEN** the user seeks the enabled RBP panel to 17.4 seconds
- **THEN** the panel submits the recording-relative cursor to the backend window path
- **AND** it displays the actual window range without submitting another Run.

### Requirement: Module ownership is independent from review composition
Review SHALL coordinate algorithm-neutral lifecycle and cursor state. Each
registered algorithm panel SHALL use an algorithm-owned adapter for request
configuration, typed result projection, units, quality and provenance display.
Review SHALL NOT instantiate the entire catalog workspace or implement
scientific calculations. The RBP adapter SHALL use the current backend RBP
definition and implementation identity and SHALL reject a response whose
recording, algorithm, configuration or window identity does not match the
immutable Review request snapshot.

#### Scenario: Open the RBP algorithm panel
- **WHEN** the user enables RBP in the Review page
- **THEN** Review creates the RBP adapter using its registered recording and selected compatible analysis montage
- **AND** the adapter obtains windows through the unified algorithm execution channel
- **AND** no frontend RBP formula or duplicate recording reader is created.

#### Scenario: RBP response contains channel quality warnings
- **WHEN** the backend returns finite RBP values with a warning for one channel
- **THEN** the panel displays that channel's values with the warning
- **AND** healthy channels remain available
- **AND** a missing, non-finite or invalid-total-power channel is shown as unavailable with its structured reason.

#### Scenario: Close the analysis panel
- **WHEN** an enabled panel is closed
- **THEN** its subscriptions and foreground reads are released
- **AND** waveform review remains operational
- **AND** immutable Runs and shared backend work are not blindly deleted or cancelled.

## ADDED Requirements

### Requirement: Register only an accepted RBP Review panel
The Review page SHALL expose only RBP as its algorithm panel, and SHALL expose
it only when its adapter and backend dynamic
window capability are declared runnable and the response contract provides
current scientific version, implementation identity, channel order, ratio
units, actual half-open sample range and quality state. Unsupported or failed
preparation SHALL be shown as an explicit unavailable state; it SHALL NOT fall
back to an older RBP executor, a full Run per cursor tick, or fabricated values.

#### Scenario: RBP capability is available
- **WHEN** the current RBP catalog entry is runnable and supports dynamic windows
- **THEN** Review offers the RBP panel
- **AND** each result is bound to the current recording, montage snapshot, configuration digest and backend window identity.

#### Scenario: RBP capability is unavailable
- **WHEN** RBP registration, capability discovery or scientific identity validation fails
- **THEN** Review keeps waveform playback available
- **AND** the RBP panel shows the structured unavailable reason
- **AND** it does not silently use a legacy path or stale result.

#### Scenario: A newer cursor replaces an in-flight RBP request
- **WHEN** the cursor advances before the previous RBP window response is bound
- **THEN** the older response cannot overwrite the newer target
- **AND** the displayed result retains its returned actual range and quality state.

#### Scenario: Progress notifications arrive between RBP results
- **WHEN** loading, progress or target-cursor properties change without a new window result
- **THEN** the panel updates status without reprojecting the previous result
- **AND** the existing per-channel charts and historical point collections are retained.

#### Scenario: A new RBP window arrives for the same session
- **WHEN** a new actual window arrives with the same recording, session and channel order
- **THEN** the panel appends or replaces that window in the existing bounded channel histories
- **AND** only visible channel chart containers are realized, with offscreen containers recycled
- **AND** closing or changing the session clears the previous foreground presentation.

#### Scenario: Review displays individual RBP channel histories
- **WHEN** the user opens the RBP panel in Review
- **THEN** it displays compact independent channel history charts labelled by channel
- **AND** chart axes retain tick marks and tick values, including percentage formatting, without axis titles
- **AND** the actual-window text and channel quality details remain accessible through tooltips
- **AND** normal presentation omits the panel title, ready-status text, window heading and channel-quality heading, as well as the aggregate bar chart and its percentage legend
- **AND** when no result is available it still displays the preparation or failure reason.
