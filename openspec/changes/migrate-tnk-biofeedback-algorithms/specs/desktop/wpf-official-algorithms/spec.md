## ADDED Requirements

### Requirement: Present a compact TNK result dashboard

The current presentation SHALL reproduce the ANT workbench chart structure using SciChart exclusively: a large shared review-style EEG waveform, chart-family tabs below it, and compact playback, score and quality controls at the right. Event annotation and event-period report controls SHALL NOT be included in this algorithm workbench. The shared waveform SHALL accept immutable, explicitly unit-labelled recording-time series and expose a seek event without owning HTTP, scientific calculations or TNK dependencies. Each tab SHALL select returned child series for meditation, BrainBeat/Flat, fatigue, HAI, RBP, aperiodic, FAA or IAPF. Scientific child module boundaries SHALL remain independent.

The RBP tab SHALL show one four-band stacked-area trend per returned source channel, using delta/theta/alpha/beta order and the ANT color mapping. Ratios SHALL be formatted as percentages without renormalization. Other metric tabs SHALL use time curves. A single returned window SHALL appear as points at actual recording seconds with an explicit single-window explanation, never categorical columns or fabricated history. On-demand display history SHALL retain at most 600 returned windows, replace duplicates, sort by time, clear on session/source/configuration changes and leave uncomputed intervals empty. Units, source channels, roles, quality and structured failures SHALL remain accessible. Full dynamic results SHALL use actual window end times in recording seconds. Missing values SHALL remain gaps, never zero. A compact result card SHALL distinguish its current-window result from its latest valid historical backend result: when the current window is unavailable, it MAY show the preceding valid value only with its actual timestamp and an explicit unavailable-current-window status. A backend child result that declares `schedule_state: not_due` MAY retain the preceding valid result until a new computed result arrives. The current on-demand target SHALL NOT fall back to an unrelated or obsolete Run; a completed matching combined background Run MAY provide its complete timeline. A completed TNK response whose cursor waiter became stale MAY add its verified result to the matching history at the backend's actual range, but SHALL NOT replace current numeric values, status, or provenance. Targets that were never computed SHALL remain gaps without interpolation.

The RBP recording-time axis SHALL use adaptive major tick intervals: approximately ten-second labels at the default full-history view, reducing to one-second labels when the operator zooms into a narrow range. While new stacked RBP windows are appended and the visible range remains at the latest data edge, the visible range SHALL advance to include the new window. A deliberate pan into historical time SHALL disable automatic advancement until the operator returns to the latest edge.

Waveform requests SHALL bind the selected immutable montage and explicit low/high/notch display settings. The backend SHALL reuse continuous zero-phase preparation before cropping a maximum 30-second response, preserve microvolts and channel order, declare display stride and enforce resource budgets. The current TNK scientific preprocessing SHALL remain its separately declared fixed 1–30 Hz contract. Source changes SHALL cancel/reject stale waveform responses and withhold the previous source's results during recomputation.

Playback display frames SHALL be separate from one-second analysis targets; drag release and Ctrl-click SHALL seek the existing coordinator. State durations SHALL be computed in the backend from returned scores and recording-time deltas, using the ANT source thresholds 55/65/75, with no interpolation across unavailable windows. The client SHALL read the immutable cumulative timeline once per Run and select its current frame locally. The algorithm workbench SHALL NOT issue recording-event requests.

#### Scenario: Inspect partial group results
- **WHEN** a TNK group returns valid values for some children and unavailable values for others
- **THEN** selecting its chart-family tab shows numeric values, chart or explicit empty reason, units and quality
- **AND** no rejected point is replaced with zero or connected across a missing window

#### Scenario: Inspect a current on-demand target
- **WHEN** the current TNK target is ready or insufficient
- **THEN** its selected child view reflects that target and actual range without falling back to an earlier full Run

#### Scenario: A superseded TNK target completes
- **WHEN** a TNK target completes after a newer cursor target has become current
- **THEN** its verified result may add a point to the matching bounded chart history at its actual backend time
- **AND** the current numeric values, status, and provenance remain bound to the newer cursor target
- **AND** any target that was never computed remains an explicit gap

#### Scenario: Change waveform filters and navigate
- **WHEN** an operator changes display filters, then seeks or plays the recording
- **THEN** the waveform request includes those exact settings and the response displays the filtered montage signal in microvolts
- **AND** chart tooltips, percentage values and linked cursor use backend values at recording seconds
- **AND** changing display filters does not silently change the frozen TNK scientific formulas

### Requirement: Expose one TNK brain-metrics group with shared mapping

The WPF algorithm list SHALL expose one user-facing “TNK 脑指标” group entry and SHALL NOT list each `tnk_*` child as an independent selectable algorithm. The group SHALL present one canonical mapping for `Fz`, `Pz`, `Oz`, `F3`, and `F4` to currently available source channels. All five roles are required because this group runs the complete fixed child set, including FAA and per-role RBP. The selected mapping SHALL be validated once, persisted as an immutable Run snapshot, and dispatched to child modules according to their declared roles. The backend catalog SHALL declare each child module's display name and required roles so the desktop can explain the dispatch.

When the operator selects this dynamic-only group, WPF SHALL select dynamic analysis and SHALL NOT present a static-analysis action. The desktop SHALL use the catalog-declared mode rather than assuming every official algorithm supports static analysis.

#### Scenario: Configure TNK brain metrics
- **WHEN** an operator opens the algorithm list
- **THEN** exactly one TNK brain-metrics group is discoverable
- **AND** the operator configures the shared role-to-source mapping once
- **AND** the UI shows which roles are required by each child metric without exposing child-specific secondary-channel fields
- **AND** the detail view shows backend-returned child values, units, quality, source channels, and structured failures

#### Scenario: Dispatch mapped inputs to TNK children
- **WHEN** the operator starts a TNK group Run
- **THEN** the coordinator dispatches the shared mapping to each selected child using only its declared roles
- **AND** each child retains independent calculation, quality, failure, and provenance ownership

### Requirement: Present TNK algorithms through catalog-driven desktop modules

The WPF official-algorithm workspace SHALL discover TNK identities, supported strategies, input roles, parameter schemas, output shapes, units, and availability from the backend catalog. Each selectable TNK algorithm SHALL use its own result handler and preview state where its output differs from existing modules. WPF SHALL submit backend-owned scientific inputs and render backend results without reproducing ANT or TNK formulas.

#### Scenario: Configure a TNK algorithm
- **WHEN** an operator selects an available TNK catalog entry
- **THEN** WPF presents only the channels, parameters, ranges, and strategies declared by that entry
- **AND** serializes the selected values into the Analysis Run request

#### Scenario: Display TNK quality and evidence
- **WHEN** a TNK Run returns values, unavailable outputs, quality evidence, or artifact references
- **THEN** WPF displays the backend values and structured quality/failure information with units and actual ranges
- **AND** it does not convert missing or rejected values into zero or recalculate scientific outputs
