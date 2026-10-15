# desktop/offline-analysis-execution-modes Specification

## ADDED Requirements

### Requirement: Offer explicit offline execution strategies

For a dynamic official algorithm, the desktop client SHALL expose full analysis by default and SHALL expose on-demand analysis only when the selected result handler declares window support and its feature flag is enabled.

#### Scenario: Full analysis is the default

- **WHEN** a user selects an algorithm that supports dynamic analysis
- **THEN** the execution strategy is `full`
- **AND** the client does not create an on-demand window subscription unless the user selects on-demand.

#### Scenario: On-demand is available

- **WHEN** the selected algorithm declares on-demand window support and its WPF feature flag is enabled
- **THEN** the dynamic execution strategy choices include full analysis and on-demand analysis.

#### Scenario: On-demand is unavailable

- **WHEN** the selected algorithm does not declare on-demand support or its feature flag is disabled
- **THEN** only full analysis is offered
- **AND** the client uses the existing full Run path.

### Requirement: Preserve scientific semantics across strategies

Changing the offline execution strategy SHALL NOT change the selected algorithm, scientific parameters, channel/montage, filters, requested range, window policy, or output units. Both strategies SHALL call the same backend algorithm module.

#### Scenario: Switch strategy

- **WHEN** a user switches between full and on-demand analysis
- **THEN** stale display state is cleared and the selected configuration is executed again
- **AND** no frontend scientific calculation is performed.
