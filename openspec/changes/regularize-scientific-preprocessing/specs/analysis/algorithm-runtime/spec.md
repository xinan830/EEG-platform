## ADDED Requirements

### Requirement: Declare supported optional preprocessing operations
The current algorithm catalogue SHALL explicitly describe supported optional preprocessing operations. Desktop configuration selection SHALL use this declaration rather than algorithm identifiers. Backend execution SHALL remain authoritative and reject unsupported operations.

#### Scenario: Review submits finite-context analysis
- **WHEN** Review prepares a request for an algorithm declaring no artifact-removal support
- **THEN** it omits ICA, SSP and EOG removal while retaining supported bad-channel and bad-segment policies and explains the scope to the user

#### Scenario: A caller bypasses desktop selection
- **WHEN** a direct request enables artifact removal for an unsupported finite-context algorithm
- **THEN** backend validation explicitly rejects the operation without publishing a successful result
