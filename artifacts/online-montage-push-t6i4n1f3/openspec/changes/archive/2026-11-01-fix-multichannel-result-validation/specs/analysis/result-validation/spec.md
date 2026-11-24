## ADDED Requirements

### Requirement: Validate declared multi-channel result order
The runtime SHALL accept a result only when the requested channel count matches the output contract role count and the returned channel representation matches that requested order.

#### Scenario: Validate a TNK structured result
- **WHEN** TNK requests five mapped source channels and returns a structured result with the same five channels in order
- **THEN** shared result validation succeeds.

#### Scenario: Reject a wrong channel count
- **WHEN** a result contract declares five roles but the request contains fewer or more channels
- **THEN** validation fails before persistence.
