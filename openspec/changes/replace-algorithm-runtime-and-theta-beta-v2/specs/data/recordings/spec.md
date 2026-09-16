## MODIFIED Requirements

### Requirement: Preserve source channel order

The system SHALL preserve the order and raw labels reported by the source file
and SHALL offer those raw labels as algorithm input choices.  It SHALL NOT
persist, infer, or expose a global semantic channel mapping.

#### Scenario: Analyze an imported O2 channel

- **WHEN** an imported recording exposes raw label `O2` and a user selects it
  for an algorithm
- **THEN** the Run records and returns `O2` as its source channel
- **AND THEN** no semantic `Oz` assignment is created

#### Scenario: List imported channels

- **WHEN** recording metadata is returned
- **THEN** channel labels appear in source-file order

### Requirement: Expose current recording resources

The system SHALL retain recording import, list, detail, preview, window,
montage, spectrum, spectrogram, playback, and read-only historical result
resources.  It SHALL remove the recording mapping resource in this approved
breaking change.

#### Scenario: Request the retired mapping route

- **WHEN** a client calls the retired recording mapping route
- **THEN** it receives a structured endpoint-retired response and no mapping
  state is created or modified

#### Scenario: Upgrade internal storage

- **WHEN** a later migration adds metadata or derived resources
- **THEN** current non-mapping recording routes remain callable with their
  existing request forms
- **AND THEN** the retired mapping storage is not required for historical
  result retrieval
