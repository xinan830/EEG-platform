# analysis/official-algorithm-migration Specification

## Purpose
Migrate official algorithms by auditable shadow computation without changing
current official results before metric-specific validation passes.
## Requirements
### Requirement: Shadow RBP before cutover

The system SHALL compare legacy and primitive RBP for every requested channel
and locked frequency band before any official RBP result path changes.

#### Scenario: RBP parity
- **WHEN** both implementations receive the same clean frozen PSD
- **THEN** the report SHALL include per-point errors and pass only within declared tolerances

### Requirement: Retain separate metric semantics

FAA, Theta/Beta, BrainBeat and IAPF SHALL retain their own quality, time and fitting
contracts during migration and SHALL NOT be coerced into a generic arithmetic
graph when doing so would alter their values.

#### Scenario: IAPF fitting
- **WHEN** IAPF has no validated 1/f fit or peak/COG result
- **THEN** its shadow result SHALL be unavailable with its established reason, not zero

### Requirement: Require an explicit spatial role mapping

A metric requiring a logical scalp role SHALL NOT silently substitute a
similarly named or positional raw channel.  Shadow evidence SHALL include the
logical-to-raw mapping used by the calculation.

#### Scenario: Posterior source is not mapped
- **WHEN** a Theta/Beta shadow run has no explicit raw channel mapped to its logical `Oz` input
- **THEN** it SHALL report `logical_oz_mapping_required` and SHALL NOT persist a fabricated comparison
