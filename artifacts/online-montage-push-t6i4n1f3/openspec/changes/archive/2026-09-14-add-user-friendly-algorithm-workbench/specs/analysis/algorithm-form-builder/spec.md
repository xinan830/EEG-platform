## ADDED Requirements

### Requirement: A readable algorithm explanation is the default view

The algorithm workbench SHALL show a Chinese-readable explanation by default
for each official algorithm. The explanation SHALL include the algorithm name,
purpose, backend calculation steps, and result/unit interpretation.

#### Scenario: User opens an official Theta/Beta definition

- **WHEN** the workbench selects Official THETA_BETA in default mode
- **THEN** it shows a Chinese explanation of the backend processing steps and
  describes the result as a dimensionless ratio
- **AND THEN** it does not expose graph adapter names such as `official_result`
  or `out`.

### Requirement: Developer details require explicit opt-in

The workbench SHALL provide a `开发者详情` control. Activating it SHALL expose
the definition form, graph nodes, units, JSON, preview, version comparison, and
authoring controls that existed before this change.

#### Scenario: Developer inspects a definition

- **WHEN** the user activates `开发者详情`
- **THEN** the previous technical editor is available
- **AND THEN** official composite definitions remain non-editable.

### Requirement: Presentation does not alter scientific execution

The workbench SHALL not change persisted definition identities, version
identities, algorithm execution, or scientific results as a result of changing
presentation modes. The frontend SHALL NOT calculate EEG-derived values.

#### Scenario: User switches display modes

- **WHEN** a user changes between readable and developer modes
- **THEN** the same selected definition and version remain selected
- **AND THEN** no scientific calculation is submitted.

### Requirement: Definition selection cannot show stale activity status

The workbench SHALL clear preview and action-status state when a different
definition is selected.

#### Scenario: User changes algorithm after validation

- **WHEN** an action message is present for one definition and the user selects another definition
- **THEN** the previous action message and preview output are not displayed for the newly selected definition.
