# analysis/official-algorithm-execution Specification

## Purpose
在可追溯 Run 管线中执行已验证的官方 IAPF 与个体化 Theta/Beta，
同时保留明确逻辑通道映射、质量门和既有离线频谱数学契约。
## Requirements
### Requirement: Run official IAPF and individualized Theta/Beta

The system SHALL execute official `iapf` and `theta_beta` through the
traceable algorithm runtime in static and dynamic modes.  New Theta/Beta Runs
SHALL use scientific contract `official-theta-beta-v2` and one explicit raw
recording channel.

#### Scenario: Static IAPF

- **WHEN** a valid recording, one selected raw channel, and analysis range are
  submitted for `iapf`
- **THEN** the completed Run returns IAPF in Hz, the selected raw channel,
  spectral evidence, quality, algorithm identity, and an Artifact

#### Scenario: Dynamic Theta/Beta

- **WHEN** a valid recording, one selected raw channel, and dynamic
  `theta_beta` configuration are submitted
- **THEN** the Run returns real window bounds and one individualized ratio per
  refresh point without fabricating values for rejected windows
- **AND THEN** every point's trend time is the end of its actual analysis
  window, not the window centre

#### Scenario: Dynamic warm-up before the selected analysis window fills

- **WHEN** playback has reached at least one complete 4 s Welch segment but
  less than the selected dynamic analysis-window duration
- **THEN** the Run evaluates the available real EEG range and returns the
  result as an explicitly marked warm-up point
- **AND THEN** once the configured analysis window is available, subsequent
  points use the fixed trailing window ending at the playback time

#### Scenario: Inspect a dynamic point

- **WHEN** a completed dynamic official Run is opened in the read-only debug
  workbench
- **THEN** its latest point includes backend-returned sampling rate, filter,
  Welch, frequency-axis, PSD, and quality evidence for that exact window

#### Scenario: Upgrade the persisted dynamic-result contract

- **WHEN** the official dynamic-result evidence or time-anchor contract changes
- **THEN** the Run cache identity changes with it
- **AND THEN** a result persisted under the prior contract is retained only as
  historical evidence and is never reused as the upgraded result

#### Scenario: Missing logical mapping

- **WHEN** Theta/Beta is requested without a global semantic mapping
- **THEN** the Run uses the explicitly selected raw channel
- **AND THEN** it does not return `OFFICIAL_CHANNEL_MAPPING_REQUIRED` or infer
  a channel by position or alias

#### Scenario: Dynamic Theta/Beta on one source channel

- **WHEN** a valid recording, raw channel `O2`, and dynamic Theta/Beta config
  are submitted
- **THEN** each returned point records its actual window range and reports one
  `Theta/Beta` value sourced from `O2`
- **AND THEN** the system does not relabel `O2` as `Oz`, compute Fz/Pz/Oz
  role outputs, or average any channels

#### Scenario: Unavailable dynamic point

- **WHEN** a dynamic window fails the PSD quality gate, lacks IAPF, or has an
  invalid Beta denominator
- **THEN** that point's value is `null` with a stable backend reason and is
  never represented as zero

### Requirement: Catalog state matches executable scope

The official catalog SHALL mark only IAPF and Theta/Beta as available and
runnable in this change. Other official algorithms SHALL retain their current
availability and runnable state.

#### Scenario: Read catalog after the cutover

- **WHEN** a client requests the official catalog
- **THEN** `iapf` and `theta_beta` are `available` and runnable
- **AND THEN** FAA and BrainBeat remain `shadow_validation` and non-runnable

### Requirement: Preserve scientific contracts

Official IAPF and Theta/Beta v2 SHALL reuse frozen offline spectral
preprocessing, quality gates, IAPF calculation, units, and time-window
semantics.  Theta/Beta v2 SHALL calculate Theta over `[max(4, IAPF-6),
IAPF-2]` Hz and Beta over `[IAPF+2,30]` Hz from the same selected channel.

#### Scenario: Match static and dynamic windows

- **WHEN** static Theta/Beta and a dynamic Theta/Beta point use the same
  selected channel and identical actual range
- **THEN** their IAPF, band powers, quality, and ratio match within the
  algorithm validation tolerance

#### Scenario: Reject an unavailable IAPF

- **WHEN** the IAPF fit produces no Peak or COG after the shared PSD quality
  gate passes
- **THEN** the output is `null` with its backend reason and is never zero
