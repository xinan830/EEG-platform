# 导联选择目录

## MODIFIED Requirements

### Requirement: stable identities appear once
The acquisition montage selector MUST contain at most one entry for each
`MontageProfile.Id` after the system and persisted profile sources are merged.
It MUST also contain at most one entry for each legacy system-preset semantic
identity, even when old persisted entries use different IDs. A semantic
identity consists of the system preset name, device signature, source channel
configuration snapshot ID, derived channel rules, and reference labels.

#### Scenario: duplicate system source entries
- **WHEN** the merged catalogue contains repeated system profiles with the same ID
- **THEN** the selector shows only the first stable identity entry
- **AND** the current system preset takes precedence over a later persisted duplicate

#### Scenario: legacy system entries with different IDs
- **WHEN** persisted system entries have different IDs but the same system-preset semantic identity
- **THEN** the current generated system preset takes precedence
- **AND** the selector shows only one entry for that semantic identity

#### Scenario: same display name with different montage rules
- **WHEN** two user profiles have the same display name but different derived channel rules
- **THEN** both profiles remain selectable

### Requirement: historical algorithm catalogues share the same boundary
The desktop review and algorithm registration paths MUST apply the same montage
identity deduplication before exposing a montage catalogue. The backend MUST
filter historical snapshot entries that have different snapshot IDs but the
same displayed signal projection as an existing recorded entry.

#### Scenario: old analysis snapshots duplicate a recorded projection
- **WHEN** an old analysis snapshot has a different ID but the same label, version, output formulas, and source mapping as a recorded montage
- **THEN** the algorithm montage selector shows one entry for that projection
- **AND** the stored historical snapshot remains available for identity-based resolution
