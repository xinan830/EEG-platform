# desktop/algorithm-detail-presentation Specification

## Purpose
TBD - created by archiving change unify-algorithm-detail-presentation. Update Purpose after archive.
## Requirements
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

