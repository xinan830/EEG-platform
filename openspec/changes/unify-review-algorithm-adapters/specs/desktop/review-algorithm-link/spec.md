## MODIFIED Requirements

### Requirement: Review owns the only playback cursor
Offline analysis panels SHALL consume the recording-relative review cursor.
They SHALL NOT run an independent playback clock or use viewport boundaries
as the scientific cursor. Playback and seeking SHALL NOT create scientific Runs
per cursor update. The review coordinator SHALL apply distinct trigger policies
for playback and timeline dragging: playback follows eligible backend-planned
targets as the cursor advances, while drag movement does not submit analysis
requests until drag completion, which submits the final cursor target.

#### Scenario: Playback advances into a new eligible window
- **WHEN** playback advances to a cursor whose policy-resolved target differs from the active target
- **THEN** review requests or observes that backend-defined window without blocking waveform playback
- **AND** it does not create a full AnalysisRun for the playback tick

#### Scenario: Drag ends at a new target
- **WHEN** the user drags the timeline and releases at a cursor that resolves to a different analysis target
- **THEN** review submits or observes the final target immediately after release
- **AND** intermediate drag positions have not generated a stream of scientific requests

#### Scenario: Drag ends at the already active target
- **WHEN** the final cursor resolves to the same target already displayed or in flight
- **THEN** review reuses that result or in-flight subscription without duplicate computation

#### Scenario: Seek a completed result
- **WHEN** the user seeks an enabled analysis panel to 17.4 seconds
- **THEN** the panel selects the latest backend window ending no later than the cursor
- **AND** it displays the actual window range without submitting another Run.

### Requirement: Resolve windows using recording-relative sample boundaries
Window selection SHALL use int64 recording-relative sample coordinates and
half-open saved ranges. Gaps SHALL retain their timeline positions. Seconds
SHALL be converted consistently using floor and SHALL NOT be rounded forward.
The backend SHALL own authoritative dynamic-window planning; the desktop SHALL
identify a cursor target but SHALL NOT infer or override scientific window
boundaries. Results SHALL expose the requested cursor separately from the
backend-resolved cursor and actual sample/time range.

#### Scenario: Cursor crosses an overlapping window endpoint
- **WHEN** a 1000 Hz recording has a saved window ending at sample 4000
- **THEN** cursor 3.999999 seconds cannot select that window
- **AND** cursors 4 and 4.000001 seconds can select it if it is the latest eligible window.

#### Scenario: The recording ends between planned steps
- **WHEN** a recording ends after the last saved endpoint but before the next step
- **THEN** review displays the last saved range and identifies its coverage end
- **AND** it does not fabricate a trailing Partial window.

#### Scenario: Backend window target is not yet eligible
- **WHEN** playback or a final drag target has not reached the first backend-planned eligible window
- **THEN** review shows an explicit insufficient-data or pending state
- **AND** it does not request a future window or display a stale result as current

### Requirement: Scrubbing retains bounded continuous feedback
Review SHALL keep timeline and waveform navigation responsive while coalescing
obsolete targets and deduplicating cached/in-flight result reads. During an
active timeline drag it SHALL update the navigation cursor but defer analysis
requests; on release it SHALL target the final cursor position. Playback SHALL
follow its configured/backend-validated refresh policy rather than issuing a
request for every render tick. A request MAY reuse cached work only when its
complete scientific and source identity matches.

#### Scenario: Drag changes cursor repeatedly
- **WHEN** the user emits many cursor movements during one drag gesture
- **THEN** the cursor and waveform navigation remain responsive
- **AND** no per-movement analysis request stream is submitted
- **AND** release submits only the latest eligible target

#### Scenario: Playback produces repeated ticks in one target interval
- **WHEN** multiple playback ticks resolve to the same backend window identity
- **THEN** they share the active request/result and do not trigger duplicate reads or computation

#### Scenario: Repeated cursor updates resolve to a cached window
- **WHEN** many drag or playback updates resolve to the same cached window
- **THEN** no additional window HTTP read or scientific Run is created.

#### Scenario: Cached target belongs to a different scientific snapshot
- **WHEN** the cursor resolves to a cached window created with a different montage, preprocessing setting, algorithm parameter, source identity or implementation identity
- **THEN** review does not reuse that window for the active configuration

### Requirement: Bind immutable analysis input snapshots to results
Each review analysis target SHALL be associated with an immutable snapshot of
recording identity, integer cursor sample, selected analysis montage/reference,
algorithm configuration, algorithm-owned preprocessing configuration, and
window policy. Display montage and display-only filtering SHALL remain separate
from scientific inputs. A result SHALL be presented only when its recording,
configuration revision, algorithm identity, and backend-resolved window identity
match the active target; provenance SHALL retain the backend-returned values.

#### Scenario: Analysis settings change while a result is pending
- **WHEN** the user changes the analysis montage or scientific configuration while a request is pending
- **THEN** the new request uses a new immutable snapshot and configuration revision
- **AND** the old response cannot replace the new target or be presented as its result

#### Scenario: Display settings change without scientific input change
- **WHEN** the user changes only waveform display montage or display filter
- **THEN** the scientific target identity remains unchanged unless that setting is explicitly part of the algorithm's scientific configuration
- **AND** display processing is not silently substituted for backend analysis preprocessing

### Requirement: Module ownership is independent from review composition
Review SHALL coordinate algorithm-neutral cursor triggers, target identity,
request lifetime, cancellation subscriptions and stale-result rejection.
Each opted-in algorithm adapter SHALL declare its supported review execution
capability, construct its complete scientific request snapshot, and parse or
present its own result shape, units, quality and provenance. Scientific formulas,
preprocessing rules, window planning and result semantics SHALL remain owned by
the corresponding backend algorithm. An algorithm SHALL NOT appear as
review-supported solely because it appears in the algorithm catalog.

#### Scenario: An opted-in algorithm window is rendered
- **WHEN** an adapter receives a ready backend result matching the current target
- **THEN** its algorithm-owned presenter binds backend-returned values, axes, units, quality and provenance
- **AND** the generic review coordinator does not contain algorithm-specific arrays or calculations

#### Scenario: An algorithm has no accepted review adapter
- **WHEN** the user opens review for an algorithm that has no accepted window capability and presenter
- **THEN** review reports that the algorithm is not available for dynamic review
- **AND** it does not silently fall back to another algorithm or run a full Run on every cursor update

#### Scenario: Close the analysis panel
- **WHEN** the analysis panel is closed
- **THEN** its subscriptions and foreground reads are released
- **AND** waveform review remains operational
- **AND** immutable Runs and shared backend work are not blindly deleted or cancelled.
