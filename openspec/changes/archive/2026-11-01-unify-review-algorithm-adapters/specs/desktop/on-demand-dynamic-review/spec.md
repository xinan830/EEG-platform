## MODIFIED Requirements

### Requirement: Reuse the existing recording cursor

Review and algorithm detail pages SHALL use the existing recording-relative sample cursor and SHALL NOT create a second playback clock. Cursor movement SHALL request or select a backend-defined target window; it SHALL NOT create a full AnalysisRun per playback tick. While playback is active, review SHALL submit the current integer cursor target once per one-second refresh interval. Backend window identity may deduplicate the resulting work, but an unchanged identity SHALL NOT suppress the cursor refresh observation. During timeline dragging, review SHALL defer drag-originated requests until release and then request the final cursor target; this suppression SHALL NOT stop the playback refresh cadence.

#### Scenario: Playback enters a new window

- **WHEN** one second of playback refresh cadence elapses
- **THEN** the desktop submits the current integer cursor through the shared window coordinator
- **AND** backend policy resolves the eligible scientific window without client-side boundary invention
- **AND** identical ready or in-flight backend identities may be reused
- **AND** waveform playback remains responsive while the analysis window is queued or computing

#### Scenario: Playback target is delayed

- **WHEN** the current target is queued, computing, or has not yet produced a response
- **THEN** review reports pending/computing/lag state separately from scientific insufficiency
- **AND** any retained result remains labeled with its actual backend range
- **AND** only a backend response with a true insufficient-data state is displayed as insufficient data

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

While observing a pending backend window, the desktop SHALL use status-aware
polling: probe promptly during the first second, then poll `queued` at 500 ms,
`preparing_source` at 250 ms, and `computing_window` at 150 ms. After remaining
in the same pending state for three seconds, it SHALL back off that state's
poll interval to 750 ms. Terminal responses SHALL stop polling immediately.
Opt-in performance diagnostics SHALL record request/window identity, state
transitions, elapsed observation time, and result-binding time without logging
signal arrays or scientific configuration contents.

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

### Requirement: Bind backend scientific configuration to an immutable review snapshot
The on-demand window response SHALL include the canonical JSON input used to derive `identity.configuration_sha256`. The canonical object SHALL identify contract `scientific-config-v1` and contain normalized `config` plus resolved `context`. The desktop SHALL verify the SHA-256 over the exact UTF-8 response bytes and compare every submitted config property against the normalized backend config before displaying a result. Missing, malformed, digest-mismatched, or request-mismatched snapshots SHALL be rejected. The published context SHALL omit local source paths and raw exception messages.

#### Scenario: Backend scientific context changes
- **WHEN** the backend resolves a montage, preprocessing, quality rule, sampling rate, or window policy that differs from the submitted target configuration
- **THEN** it returns the canonical resolved scientific configuration and its digest in the same window response
- **AND** the desktop binds the verified canonical configuration to its immutable review snapshot before accepting the result.

#### Scenario: Scientific snapshot cannot be verified
- **WHEN** the response omits the canonical configuration, contains invalid JSON, or its digest does not match
- **THEN** the desktop rejects the result and reports an identity validation failure.
