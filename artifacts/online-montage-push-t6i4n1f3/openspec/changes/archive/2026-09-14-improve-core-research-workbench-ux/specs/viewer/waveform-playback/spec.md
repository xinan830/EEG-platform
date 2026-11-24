## ADDED Requirements

### Requirement: Workbench navigation identifies core research tasks

The local workbench SHALL provide visible in-page navigation for waveform
review, spectrum analysis, time-frequency analysis, and results. Activating a
navigation item SHALL move the user to the corresponding workbench section
without changing recording data or submitting an analysis request.

#### Scenario: User selects spectrum analysis

- **WHEN** a recording is open and the user selects the spectrum navigation item
- **THEN** the workbench scrolls to the spectrum section
- **AND THEN** the waveform playback and analysis configuration remain unchanged.

### Requirement: Developer diagnostics require explicit opt-in

The workbench SHALL default to the ordinary research view. Render and transport
diagnostic output SHALL only be visible after the user explicitly enables
developer mode.

#### Scenario: User opens a recording

- **WHEN** the ordinary workbench view is shown
- **THEN** runtime debug information is not displayed
- **AND THEN** it becomes visible only after developer mode is enabled.

### Requirement: Viewer controls disclose their scope

The visible display-controls area SHALL identify itself as applying to waveform
review only and SHALL state that it does not change the offline frequency or
time-frequency analysis contract.

#### Scenario: User adjusts waveform high cut

- **WHEN** a user changes a waveform display filter
- **THEN** the interface identifies it as a viewer-only setting
- **AND THEN** it does not claim to change the offline analysis pipeline.
