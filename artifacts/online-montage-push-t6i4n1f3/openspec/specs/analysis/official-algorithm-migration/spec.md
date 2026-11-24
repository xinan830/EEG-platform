# analysis/official-algorithm-migration Specification

> 状态：历史迁移阶段规范。下方 shadow 比较条款记录当时的切换门槛，已由 [退役实现移除规范](../retired-algorithm-removal/spec.md)取代，不再要求提供 shadow 计算器或旧版本。当前输入/结果边界见 [算法 Runtime](../algorithm-runtime/spec.md)；空间角色、不可伪造数值等仍有效的约束按当前规范执行。此次仅补充状态导航，不重新启用或修改科学行为。

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
