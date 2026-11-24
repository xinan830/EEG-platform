## ADDED Requirements

### Requirement: Review panel identity matches its algorithm execution route
Each exposed Review algorithm panel SHALL bind its own algorithm ID, parameter configuration, result projection, and matching execution adapter. Selecting a panel SHALL submit the selected algorithm ID, retain the backend-declared scientific version and ordered channels, and display only results accepted by the existing recording, configuration and window identity checks. Shared presentation templates SHALL NOT change the selected algorithm, units, absolute sample range, quality state or provenance. A missing or mismatched adapter SHALL fail explicitly rather than route to another algorithm.

#### Scenario: Select an algorithm with a shared scalar template
- **WHEN** the user selects IAPF, Theta/Beta or either BrainBeat algorithm
- **THEN** its own algorithm ID and configuration are sent through its matching adapter
- **AND** the scalar template presents the corresponding registered result projection.

#### Scenario: Select an algorithm with specialized capabilities
- **WHEN** the user selects PSD, RBP or TNK
- **THEN** its specialized adapter and result presentation remain attached to that algorithm
- **AND** shared lifecycle coordination does not replace its scientific calculation route.

#### Scenario: An execution adapter does not match the registered panel
- **WHEN** Review composition supplies an adapter with a different algorithm ID than its panel registration
- **THEN** composition fails with an explicit identity error before an analysis request is made
- **AND** no other algorithm's result is used as a substitute.
