# analysis/official-algorithm-execution Specification

## Purpose
在可追溯 Run 管线中执行已验证的官方 IAPF 与个体化 Theta/Beta，
同时保留明确逻辑通道映射、质量门和既有离线频谱数学契约。
## Requirements
### Requirement: Run official IAPF and individualized Theta/Beta

The system SHALL execute official `iapf` and `theta_beta` through the traceable
Run pipeline in static and dynamic modes.

#### Scenario: Static IAPF

- **WHEN** a valid recording and analysis range are submitted for `iapf`
- **THEN** the completed Run returns an IAPF value in Hz, spectral evidence,
  quality, algorithm identity, and an Artifact

#### Scenario: Dynamic Theta/Beta

- **WHEN** a valid recording with an explicit Fz/Pz/Oz mapping is submitted
  for dynamic `theta_beta`
- **THEN** the Run returns real window bounds and one individualized ratio per
  refresh point without fabricating values for rejected windows

#### Scenario: Missing logical mapping

- **WHEN** Theta/Beta is requested without an explicit saved Oz mapping
- **THEN** the Run fails with `OFFICIAL_CHANNEL_MAPPING_REQUIRED`
- **AND THEN** no channel is inferred by position or alias

### Requirement: Catalog state matches executable scope

The official catalog SHALL mark only IAPF and Theta/Beta as available and
runnable in this change. Other official algorithms SHALL retain their current
availability and runnable state.

#### Scenario: Read catalog after the cutover

- **WHEN** a client requests the official catalog
- **THEN** `iapf` and `theta_beta` are `available` and runnable
- **AND THEN** FAA and BrainBeat remain `shadow_validation` and non-runnable

### Requirement: Preserve scientific contracts

Official execution SHALL reuse the existing offline spectral preprocessing,
quality gate, IAPF calculation, and individualized Theta/Beta calculation
modules without changing their mathematical definitions, units, channel order,
or time-window semantics.

#### Scenario: Reject an unavailable IAPF

- **WHEN** the IAPF fit produces no Peak or COG after the shared PSD quality
  gate passes
- **THEN** the output is `null` with its backend reason and is never zero
