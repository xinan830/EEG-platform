## MODIFIED Requirements

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
