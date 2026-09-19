# analysis/official-rbp-faa-brainbeat-execution Specification

## Purpose
TBD - created by archiving change enable-rbp-faa-brainbeat. Update Purpose after archive.
## Requirements
### Requirement: Execute official RBP as one four-band result

The system SHALL execute official `rbp` for one explicitly selected raw
channel in static mode through the traceable Run pipeline. It
SHALL return Delta, Theta, Alpha and Beta relative power values from the same
PSD, quality gate and 1-30 Hz denominator.

#### Scenario: Static RBP result

- **WHEN** a user submits a valid recording, raw channel and static range for
  `rbp`
- **THEN** the completed Run returns all four backend-computed relative powers,
  their units, the source channel, actual range, PSD evidence and an Artifact
- **AND THEN** the frontend renders those returned values without recalculating
  band integrals or percentages

#### Scenario: RBP quality failure

- **WHEN** an RBP PSD window fails its quality gate
- **THEN** all four band-share values are `null` with the backend reason
- **AND THEN** no band is represented as zero or carried from a prior point

### Requirement: Execute FAA with explicit paired channels

The system SHALL execute official `faa` only for explicit distinct raw
`f3_channel` and `f4_channel` selections. It SHALL preserve the existing
paired two-second epoch quality gate and return `ln(P_F4) - ln(P_F3)`.

#### Scenario: Static FAA result

- **WHEN** both selected source channels exist and meet the paired FAA quality
  contract
- **THEN** the Run returns FAA, F3/F4 alpha powers, epoch counts, clean ratio,
  actual range and source-channel identities
- **AND THEN** it analyses exactly the requested absolute range without
  applying the legacy realtime pipeline's initial-recording discard

#### Scenario: Missing or unsuitable FAA source channels

- **WHEN** either selected channel is missing, both selections are identical,
  or the paired quality gate fails
- **THEN** the Run returns a structured failure or `null` FAA value with its
  paired-quality reason
- **AND THEN** it does not infer a replacement channel or produce zero

### Requirement: Preserve the stateful BrainBeat boundary

The catalog SHALL continue to expose BrainBeat as `shadow_validation` and
non-runnable until a future change defines the realtime session, IAPF-lock and
EMA persistence contract.

#### Scenario: Read BrainBeat in the catalog

- **WHEN** a client loads the official algorithm catalog
- **THEN** BrainBeat explains that it is under engineering validation and is
  disabled
- **AND THEN** it cannot submit an offline Run using a stateless substitute
