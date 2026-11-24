## ADDED Requirements

### Requirement: Reuse identity-validated continuous FAA sources across windows
FAA dynamic windows SHALL share a bounded owner for continuous preprocessing of the configured ordered pair, rather than rereading and filtering the whole recording in every window context. The source SHALL retain V/float64 samples, continuous zero-phase filter semantics, immutable raw data, gap/quality behavior and actual sample bounds. Reuse SHALL require matching source, montage/reference, filters, channel order, units and implementation identity.

#### Scenario: Adjacent independent FAA windows use the same scientific input
- **WHEN** separate window contexts request adjacent FAA ranges with the same source and preprocessing identity
- **THEN** whole-recording read/filter preparation occurs once and the resulting values agree with independent continuous filtering followed by the same slices

#### Scenario: Source or preprocessing identity changes
- **WHEN** source content, montage/reference, selected pair/order, units, filters or implementation changes
- **THEN** an earlier prepared source is not reused, and failed or cancelled preparation does not become a valid cached source

### Requirement: Terminal subscription cleanup does not gate result delivery
The desktop observer SHALL deliver an observed terminal window independently of its bounded consumer-subscription release, logging cleanup failures. Pending observations cancelled by the user SHALL still release only their own subscription using an independent bounded cleanup token.

#### Scenario: Completed window has a slow release endpoint
- **WHEN** the result is terminal and subscription release is delayed
- **THEN** the consumer receives the completed window immediately while release continues with a bounded token and observed errors
