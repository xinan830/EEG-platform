## ADDED Requirements

### Requirement: Project list presents a clear visual hierarchy

The project list SHALL allocate more width to project names and descriptions than to compact metadata columns. Selection SHALL use a clear brand indicator without a dominant full-row brand fill, keyboard focus SHALL remain visible, and row actions SHALL retain accessible hit targets.

#### Scenario: View project rows

- **WHEN** the project list displays one or more projects
- **THEN** the name and description columns SHALL receive more space than count and status columns
- **AND** selecting a row SHALL show a blue edge indicator while preserving readable text and visible keyboard focus
- **AND** all existing project row actions SHALL remain available.
