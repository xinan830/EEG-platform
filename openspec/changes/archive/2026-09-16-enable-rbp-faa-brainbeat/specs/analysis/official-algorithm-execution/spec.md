## MODIFIED Requirements

### Requirement: Run official IAPF and individualized Theta/Beta

The system SHALL execute official IAPF, Theta/Beta, RBP and FAA through their
own registered backend modules and the existing Run lifecycle. Algorithm
modules SHALL declare their client-safe inputs, output unit/shape, time
semantics and quality behavior.

#### Scenario: Select an executable official algorithm

- **WHEN** a client selects a catalog item marked runnable
- **THEN** it submits only the parameters declared by that item's backend
  schema
- **AND THEN** the Run records actual inputs, output evidence and the module's
  scientific and implementation identities

#### Scenario: Preserve a specialized algorithm contract

- **WHEN** RBP, FAA, IAPF or Theta/Beta execute
- **THEN** each algorithm retains its own mathematical formula and quality
  rules
- **AND THEN** the shared runtime does not coerce multi-value RBP or paired FAA
  into an unrelated single-channel scalar contract

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
