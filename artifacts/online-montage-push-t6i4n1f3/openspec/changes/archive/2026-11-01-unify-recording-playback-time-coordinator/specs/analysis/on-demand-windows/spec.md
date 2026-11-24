## MODIFIED Requirements

### Requirement: Ensure a backend-planned dynamic window

The system SHALL provide an opt-in window capability only for algorithms that declare a validated window executor. The desktop SHALL obtain the recording-relative integer cursor sample from the active recording-session time coordinator and SHALL identify the recording, algorithm definition/version and complete scientific configuration. The backend SHALL resolve the scientific window plan and stable window identity. Existing full Run endpoints and algorithms without the capability SHALL retain their current behavior.

#### Scenario: Request a supported PSD window

- **WHEN** the active coordinator publishes a valid cursor for a supported PSD algorithm
- **THEN** the client submits that cursor with the scientific identity through the window request coordinator
- **AND** the backend returns a stable session/window identity, backend-planned half-open sample range, current lifecycle state, request identity, and a way to observe that work
- **AND** the client cannot override the authoritative scientific window boundaries.

#### Scenario: Algorithm has no window capability

- **WHEN** a client receives a coordinator cursor for an algorithm that has not declared the validated capability
- **THEN** the backend returns a structured unsupported-capability response
- **AND** it does not silently run a full AnalysisRun or fabricate an empty window.

#### Scenario: Playback context changes before response

- **WHEN** a window response returns after the recording, generation, configuration, or cursor target has changed
- **THEN** the desktop rejects the response as obsolete
- **AND** it does not replace the current visible scientific result.
