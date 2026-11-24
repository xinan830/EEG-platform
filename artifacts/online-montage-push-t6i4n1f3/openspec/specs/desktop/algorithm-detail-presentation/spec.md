# desktop/algorithm-detail-presentation Specification

## Purpose
TBD - created by archiving change unify-algorithm-detail-presentation. Update Purpose after archive.
## Requirements
### Requirement: Present analysis notch as an algorithm-owned setting
The desktop SHALL expose the backend-declared spectral notch choice in the algorithm analysis configuration, default it to 50 Hz when declared by the current contract, and provide an explicit off choice. It SHALL serialize that choice with the algorithm run request. The control SHALL remain independent of review waveform filter controls and SHALL NOT alter display filtering.

#### Scenario: Submit with the default notch
- **WHEN** the user submits a spectral algorithm without changing its analysis notch setting
- **THEN** the request carries the declared 50 Hz default.

#### Scenario: Submit with notch disabled
- **WHEN** the user selects the explicit off option
- **THEN** the request carries a null or off value
- **AND** the display waveform filter setting remains unchanged.

### Requirement: Shared cards use named presentation resources
Execution, quality, provenance and dynamic result cards SHALL use common named
frame and heading styles from the existing analysis resource dictionary. They
SHALL retain their scientific bindings and SHALL NOT add implicit global styles
for unrelated Border or TextBlock controls.

#### Scenario: Change the common frame appearance
- **WHEN** an analysis card overrides the shared divider brush
- **THEN** its outer frame uses the override
- **AND** execution commands and scientific values remain owned by its existing context.

#### Scenario: Load a shared card independently
- **WHEN** a shared card is instantiated without the application main window
- **THEN** its frame and title resources resolve using its explicit dictionary imports.

### Requirement: Visual sharing does not merge playback ownership
Sharing styles SHALL NOT change detail-page dynamic playback or introduce an
independent algorithm clock in offline review. Algorithm charts, unit projections
and scientific window policies SHALL remain module-owned.

#### Scenario: Render dynamic result rows
- **WHEN** the dynamic result card receives its existing detail context
- **THEN** row bindings remain unchanged
- **AND** resource reuse does not submit Runs or modify the playback cursor.

