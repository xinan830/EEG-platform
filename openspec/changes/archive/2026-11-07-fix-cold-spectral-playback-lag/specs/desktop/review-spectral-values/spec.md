## MODIFIED Requirements

### Requirement: Preserve result pairing and playback responsiveness

The custom panel SHALL use the existing PSD/RBP playback analysis policy, segmentation, filters, reference, recording, actual sample bounds, and ordered channels. Cursor movement and range adjustment SHALL project the already calculated current-window source and SHALL NOT start a new Welch window calculation for every interaction. A stale or mismatched projection SHALL NOT overwrite newer values. The existing five-band panel and topomap routes SHALL remain independently usable.

#### Scenario: PSD and RBP windows do not match
- **WHEN** the PSD and RBP sources differ in recording, actual sample bounds, channel order, or compatible analysis settings
- **THEN** the panel rejects the pair or marks affected values unavailable and does not combine them into valid-looking rows

#### Scenario: A projection response is delayed
- **WHEN** the user changes a range or playback window while a custom projection is outstanding
- **THEN** waveform playback, seek and viewport loading do not wait for custom-range projection completion and a late response cannot restore values for an older selection or window

#### Scenario: Adjust a frequency input with hover arrows
- **WHEN** the pointer enters a point or range endpoint input
- **THEN** compact increase/decrease buttons appear inside the input without shifting its layout
- **AND** clicking or holding a button steps through actual backend bins; keyboard Up/Down performs the same selection
- **AND** stepping stops at the frequency axis limits and cannot cross the other range endpoint; unavailable bins disable stepping and invalid text is not silently discarded
- **AND** selection continues through the existing PSD/RBP projection lifecycle without a new calculation route

#### Scenario: A source value is unavailable
- **WHEN** a PSD or RBP source is missing, unknown or rejected, or its quality, data or provenance makes a channel value unavailable
- **THEN** that value is displayed as unavailable with the source reason, without fabricating zero or reusing another channel/window

#### Scenario: First open at a fractional playback second
- **WHEN** the backend returns the latest valid step-aligned PSD/RBP window before the requested fractional cursor
- **THEN** the numeric panel validates and projects those matching actual windows without requiring the returned cursor to equal the requested sample
- **AND** the displayed bounds and provenance identify the actual source window

#### Scenario: One source is slower than the playback refresh step
- **WHEN** new playback targets arrive while a paired PSD/RBP source or projection is still loading
- **THEN** the numeric-panel owner completes the active pair, retains only the newest pending foreground target for both sources, and never waits on the waveform clock; future preparation SHALL remain bounded to two upcoming targets and SHALL NOT publish before its playback target
- **AND** queued preparation older than the newest pending foreground target is discarded before it starts, so missed deadlines cannot create a backlog of obsolete preparation
- **AND** waiting for a successor source/bin response preserves the last published rows and curve with their actual time and an explicit pending status, rather than repeatedly clearing them or presenting them as the newest target
- **AND** explicit seek, timeline drag, configuration change and close invalidate the displayed pair and reject obsolete responses immediately

#### Scenario: Edit the range while the next bin grid is loading
- **WHEN** a successor bin response is pending and the user changes the selected range or switches to range mode
- **THEN** the panel retains the selection and waits for that bin response before requesting its range projection
- **AND** it never applies a successor range response against the retained previous window's bin grid

#### Scenario: Reuse a historical montage catalogue during playback
- **WHEN** repeated window requests use the same recording, manifest content and saved snapshot contents
- **THEN** the backend reuses a bounded validated catalogue without opening one database connection per saved snapshot
- **AND** changing the manifest, saved snapshots or source directory invalidates that reuse, and returned values cannot mutate the stored catalogue

#### Scenario: Resolve a slow request while another window is admitted
- **WHEN** one request is resolving recording metadata and scientific configuration
- **THEN** that planning work does not hold the shared window admission gate
- **AND** capacity, duplicate-window admission, recovery identity and shutdown fencing remain enforced

#### Scenario: Cancel or replace the lease of an executing window
- **WHEN** another database connection cancels the window or invalidates its exact attempt token or expiry
- **THEN** the next cooperative checkpoint reads that committed state without a stale local cancellation cache
- **AND** the worker closes its owned state-reader connection on completion or failure and stale results cannot publish

#### Scenario: Prepare cold offline playback windows
- **WHEN** either numeric panel is playing and upcoming recording samples are available
- **THEN** the existing PSD/RBP computation and backend numeric projection routes prepare at most two upcoming step-aligned pairs without changing the current displayed sources
- **AND** at the matching playback position prepared results are adopted with the same recording, actual sample bounds, ordered channels, units, quality and provenance checks as demand-loaded results
- **AND** range results are reused only for matching selected bounds, while frequency selection uses returned bins without frontend scientific calculation

#### Scenario: Cancel future preparation
- **WHEN** playback pauses, the user seeks or drags the timeline, analysis configuration changes or the panel closes
- **THEN** outstanding preparation is cancelled and discarded, no old response can change current values, and no further future targets are scheduled until valid playback resumes

#### Scenario: Preparation misses a deadline
- **WHEN** a cold window is still computing at its playback target
- **THEN** waveform playback continues independently, the panel retains the previous actual window with explicit pending state, and the delayed result cannot be labelled as a different window
- **AND** diagnostics distinguish preparation duration, publication wait and actual window lag
