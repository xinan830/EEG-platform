## ADDED Requirements

### Requirement: Algorithm-owned WPF run integration
The WPF common Run orchestration SHALL resolve an explicitly registered
algorithm adapter for algorithm-specific input checks and completion
integration. It SHALL NOT add concrete algorithm ID branches for those
responsibilities. Scientific computation SHALL remain in the backend.

#### Scenario: Add an algorithm using existing scalar capabilities
- **WHEN** an algorithm supplies its own input and result adapter and is registered
- **THEN** common Run orchestration invokes that adapter without a new algorithm ID branch
- **AND** the backend request retains the declared algorithm ID and scientific version

### Requirement: Module-owned scalar window adaptation
The IAPF, Peak Frequency and RBP modules SHALL parse their backend window
results and pass generic window rows to the shared playback timeline.
Existing initial-cursor, unavailable-result and rerun behavior SHALL be
preserved during this structural refactor.

#### Scenario: A rejected backend window reaches the cursor
- **WHEN** the cursor reaches a backend-rejected scalar window
- **THEN** the module retains the rejection reason and unavailable value
- **AND** the shared timeline receives the backend window bounds without recomputing science
