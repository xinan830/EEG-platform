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
