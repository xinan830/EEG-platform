## Purpose

Let users inspect backend-computed scalar algorithm windows beside a recording waveform while reusing Review's recording identity, cursor, and playback lifecycle.

## ADDED Requirements

### Requirement: Review exposes the four declared scalar algorithms as one-at-a-time panels
Recording Review SHALL offer FAA, IAPF, Theta/Beta, and Brainbeat panels only when the matching current catalog algorithm is runnable and declares on-demand dynamic windows. Each panel SHALL use Review's registered recording and recording-relative sample cursor through the shared dynamic-window adapter. At most one algorithm panel SHALL be active. Opening, switching, or closing a panel SHALL NOT create another recording reader, playback clock, or full AnalysisRun per cursor update.

Each scalar panel and the RBP panel SHALL begin its visible time range at the Review cursor captured when that panel is opened. The range SHALL expand as playback advances and SHALL NOT display blank history before the panel was opened.

#### Scenario: Open a scalar algorithm panel during playback
- **WHEN** the user selects FAA, IAPF, Theta/Beta, or Brainbeat from Review
- **THEN** Review submits the active recording, declared scientific configuration, and current cursor to the on-demand dynamic-window path
- **AND** waveform playback and its time coordinator continue independently.

#### Scenario: Open a panel after playback has advanced
- **WHEN** the user opens an RBP or scalar algorithm panel at recording time 30 seconds
- **THEN** the chart's visible time range starts at 30 seconds and expands as playback advances
- **AND** the chart does not show the empty 0-to-30-second interval.

#### Scenario: Switch or close while a request is pending
- **WHEN** the user switches to another algorithm or closes the active panel while its request is pending
- **THEN** the old panel detaches and its foreground subscription is released
- **AND** a late response cannot replace the active panel or change the Review cursor.

### Requirement: Algorithm modules own their Review input configuration
Each Review algorithm configuration SHALL use the current catalog version and dynamic window/refresh policy. IAPF and Theta/Beta SHALL serialize one explicitly selected source channel. FAA SHALL serialize two distinct source channels in F3 then F4 role order. Brainbeat SHALL serialize two distinct source channels in Fz then Pz role order. When a compatible montage is selected, each submitted output mapping SHALL include only the requested source outputs and preserve their registered roles, units, and montage identity. Missing or incompatible role mappings SHALL be reported as unavailable and SHALL NOT be replaced with another channel.

#### Scenario: Configure a single-channel algorithm
- **WHEN** IAPF or Theta/Beta is opened or its selected channel changes
- **THEN** the immutable request contains exactly that channel, its montage mapping when applicable, the catalog scientific version, and declared dynamic policy.

#### Scenario: Configure a paired-channel algorithm
- **WHEN** FAA or Brainbeat is opened or either source channel changes
- **THEN** the request contains the two distinct channels in the algorithm's declared role order and matching montage mappings
- **AND** Review retains all compatible choices without inferring a role from list position.

### Requirement: Scalar Review panels preserve returned value, time, quality, and provenance contracts
The owning algorithm presentation SHALL parse only its declared output field and unit from a current identity-matching dynamic-window response. It SHALL preserve the returned recording-relative actual window range, state, quality, structured failure reason, scientific version, and algorithm-specific provenance. Review SHALL display backend-returned values only and SHALL NOT recalculate EEG metrics. A null, rejected, unavailable, failed, malformed, or stale response SHALL NOT be converted to zero or represented by a previous value as the current target.

#### Scenario: Present a valid scalar window
- **WHEN** a clean response for the active algorithm, recording, configuration, and cursor arrives with the declared output and unit
- **THEN** the panel plots that backend value at its returned recording-relative time and actual window range
- **AND** its axis and tooltip expose the declared unit without altering the numeric value.

#### Scenario: Present an unavailable or malformed window
- **WHEN** a response has no numeric value, fails quality, omits required identity/provenance, uses the wrong output or unit, or belongs to an obsolete target
- **THEN** the panel clears any value that could be mistaken for the current target and displays the concise backend or validation reason
- **AND** the waveform remains available.

#### Scenario: Change configuration while paused
- **WHEN** the user changes an algorithm source channel while Review is paused
- **THEN** Review preserves the same recording cursor and playback state
- **AND** an immutable new configuration is requested and results from the prior configuration cannot replace it.
