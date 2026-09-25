# analysis/peak-frequency-band-ratio Specification

## Purpose
TBD - created by archiving change add-peak-frequency-band-ratio. Update Purpose after archive.
## Requirements
### Requirement: Register separate official modules

The backend SHALL expose `peak_frequency` and `band_ratio` as separate
registered official modules with stable IDs, scientific versions,
implementation identities, typed parameter schemas, supported modes, quality
contracts, and output schemas.

#### Scenario: Inspect the catalog

- **WHEN** a client reads the official algorithm catalog
- **THEN** both modules expose their identity, parameters, units, supported
  static/dynamic modes, and implementation identity

#### Scenario: Reject an unavailable module version

- **WHEN** a request names an unregistered module or unavailable version
- **THEN** the backend rejects it before EEG loading or queue execution

### Requirement: Compute a parameterized peak frequency

The `peak_frequency` module SHALL require an explicit frequency band and
explicit ordered source channels. It SHALL select the maximum PSD value on the
declared frequency grid within the band using its versioned edge, tie,
interpolation, and insufficient-bin policy. Its result SHALL be identified as
peak frequency and SHALL NOT be labeled IAPF or Alpha unless the IAPF contract
is selected.

#### Scenario: Compute a Beta peak

- **WHEN** a valid PSD is requested for a declared Beta band
- **THEN** the result contains `peak_frequency_hz`, the selected grid/bin
  evidence, resolved band, channel order, units, and quality evidence

#### Scenario: No valid frequency bin

- **WHEN** the requested band contains no valid frequency bin
- **THEN** the result is unavailable with a stable reason and no numeric zero

### Requirement: Compute a parameterized band ratio

The `band_ratio` module SHALL require explicit numerator and denominator bands,
integrate each band through the authoritative band-power primitive, and return
the numerator divided by the denominator with dimensionless `ratio` units. The
resolved bands and integration evidence SHALL be persisted.

#### Scenario: Compute a valid ratio

- **WHEN** both bands are valid and denominator power is positive
- **THEN** the result contains the ratio, both integrated powers, resolved bands,
  boundary policy, units, and quality evidence

#### Scenario: Denominator is unavailable

- **WHEN** denominator power is zero, non-finite, or rejected by quality policy
- **THEN** the result is unavailable with a stable reason and SHALL NOT contain
  infinity, NaN, or a fabricated zero

### Requirement: Preserve canonical units and coordinates

Both modules SHALL use V/float64 input, `V^2/Hz` PSD, `V^2` band power, `Hz`
frequency, and dimensionless ratio units. Every request SHALL resolve to the
recording-relative half-open sample range
`[start_sample_inclusive, end_sample_exclusive)` before execution, caching, or
event alignment.

#### Scenario: Resolve a seconds request

- **WHEN** the UI submits seconds and a validated sampling rate
- **THEN** the backend persists the resolved sample range and derives display
  seconds from that range without using display state as scientific identity

### Requirement: Share static, dynamic, and box-selection execution

Static mode SHALL run one resolved sample range. Dynamic mode SHALL use one
analysis range with sample-aligned window and step, and every point SHALL report
its actual bounds, anchor, quality, and availability state. Box selection SHALL
create a static request through the same Runtime path.

#### Scenario: Compare a boxed range with static execution

- **WHEN** a user selects a box corresponding to a resolved sample range
- **THEN** the result uses the same module, parameters, primitive versions,
  provenance, and quality path as an equivalent static request

### Requirement: Preserve quality and gap semantics

The modules SHALL report DataIntegrity, SignalQuality, and AlgorithmValidity.
Recording gaps SHALL NOT be imputed. Transform padding, when allowed by the
underlying primitive, SHALL be recorded separately from gap handling.

#### Scenario: Analyze a gapped window

- **WHEN** a static or dynamic window contains a recording gap
- **THEN** the module applies its declared policy or returns unavailable with a
  reason, and never fabricates EEG samples

### Requirement: Preserve provenance and historical readability

Every result SHALL retain the complete validated parameter snapshot, module and
scientific version, implementation identity, configuration hash, channel order,
preprocessing and primitive versions, resolved bands, requested and actual
sample ranges, output schema, and quality evidence. Existing IAPF,
Theta/Beta, and historical Run results SHALL remain readable and semantically
unchanged.

#### Scenario: Read a historical IAPF result

- **WHEN** a historical Run is loaded after these modules are installed
- **THEN** its original module identity, parameters, units, coordinates, and
  result semantics remain unchanged
