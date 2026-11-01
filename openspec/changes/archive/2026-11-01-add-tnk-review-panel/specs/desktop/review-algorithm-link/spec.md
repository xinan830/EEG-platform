## ADDED Requirements

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
