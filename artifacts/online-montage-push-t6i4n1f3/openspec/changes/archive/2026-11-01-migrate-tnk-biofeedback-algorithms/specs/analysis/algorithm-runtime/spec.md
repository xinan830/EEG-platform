## ADDED Requirements

### Requirement: Replay declared offline algorithm context for on-demand results

The runtime SHALL allow a history-dependent offline algorithm module to declare the canonical context needed to compute a requested target window. On-demand execution SHALL replay only the module-declared context using canonical sample-aligned scheduling and deterministic state transitions, return the requested target result, and include the effective context identity in cache and provenance. Shared orchestration SHALL own cancellation, status, target identity, persistence, and reuse; it SHALL NOT implement algorithm-specific state transitions.

#### Scenario: Compute a target requiring prior windows
- **WHEN** an on-demand request targets a window whose declared algorithm state depends on earlier windows in the same run context
- **THEN** the runtime supplies the module with the canonical context from its declared reset boundary through the target
- **AND** only the requested target result is published as the on-demand result

#### Scenario: Prevent reuse across different histories
- **WHEN** two target requests share target bounds but differ in reset boundary, preceding data, state configuration, or algorithm identity
- **THEN** their context identities differ and the runtime does not reuse the prior result

#### Scenario: Cancel context replay
- **WHEN** an on-demand state replay is cancelled before the target completes
- **THEN** the runtime stops scheduling remaining context windows, releases resources, and persists no successful target result
