## MODIFIED Requirements

### Requirement: Persist runtime algorithm provenance

Each runtime algorithm Run SHALL persist the selected algorithm ID, source,
scientific version, implementation identity, configuration digest, explicit raw
input channels, requested range, actual range, window semantics, units, and
quality result.

#### Scenario: Read a completed Theta/Beta v2 Run

- **WHEN** a completed Theta/Beta v2 Run is retrieved
- **THEN** provenance identifies `official-theta-beta-v2`, the selected raw
  channel, one actual analysis range per output point, and the frozen spectral
  contract used to obtain its PSD

### Requirement: Preserve unavailable value provenance

The provenance of a runtime output with no scientific value SHALL retain its
structured quality or calculation reason while its numerical field is `null`.

#### Scenario: Read a rejected output

- **WHEN** a result has no valid Theta/Beta value
- **THEN** its persisted value is `null` and its reason is distinguishable from
  measured zero
