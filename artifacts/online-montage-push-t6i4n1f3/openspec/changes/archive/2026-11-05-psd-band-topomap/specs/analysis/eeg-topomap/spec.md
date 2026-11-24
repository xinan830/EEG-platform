## MODIFIED Requirements

### Requirement: Topomap input is an explicit windowed channel result
Topomap input MUST be one selected scalar spatial field from an explicit file-absolute-time algorithm result. It MUST include `recording_id`, result/window identity, `window_start_s`, `window_end_s`, original channel order, one value and quality state per channel, a stable field identity, an explicit unit, algorithm version, and the bound montage snapshot identity. A frequency-resolved source MUST identify the selected frequency operation: an exact bin with its `frequency_hz`, or a band summary with its exact `frequency_range_hz`, frequency reduction, and output unit. Internal values MUST follow the platform scientific-unit contract; public fields MUST declare their unit. The topomap MUST NOT infer time, channel positions, field identity, or values from the display clock or frontend.

#### Scenario: Algorithm supplies a valid band result
- **WHEN** an algorithm provides one complete window of per-channel values for a band or other scalar metric
- **THEN** its adapter preserves the window identity, original channel order, field identity, unit, algorithm version, montage identity, and per-channel quality
- **AND** the spatial projection consumes only that field's values

#### Scenario: PSD supplies one band-mean density
- **WHEN** PSD supplies its ordered channel-by-frequency density and a consumer selects one canonical band
- **THEN** the adapter integrates the piecewise-linear density at the exact band edges independently for each channel and divides by the complete band width
- **AND** it reports the exact band identity and frequency bounds with unit `V^2/Hz`
- **AND** it does not read RBP result arrays or start another algorithm run

#### Scenario: PSD supplies one actual frequency bin
- **WHEN** PSD supplies its ordered channel-by-frequency result and a consumer selects one returned frequency bin
- **THEN** the adapter selects that exact bin for every channel and reports its exact `frequency_hz` with unit `V^2/Hz`
- **AND** it does not integrate, average, interpolate, or substitute values across frequency bins

#### Scenario: Result has no usable numeric value
- **WHEN** a channel value is missing, non-finite, quality-invalid, or its result window is incomplete
- **THEN** that channel is marked unavailable and excluded from spatial interpolation
- **AND** the system does not substitute zero, a previous window, a neighboring channel, or a value from another frequency bin

### Requirement: Topomap transport uses explicit scalar field identifiers
The current topomap request MUST name one or more unique `field_ids` bound to a current result identity. The response MUST return those scalar spatial fields under `fields`, with each field's identifier, label, unit, field-specific metadata, per-channel points, quality, grid, and provenance. Frequency fields MUST report the exact selected `frequency_hz`. PSD band-mean-density fields MUST report the canonical band, exact frequency bounds, and complete band width used to average the integral. The current transport MUST NOT use the former `bands`, `power_type`, or `band_id` contract, infer a default field when `field_ids` are absent, or silently map an unknown field to another field. Unsupported identifiers MUST produce a structured invalid-request response; a known field with no usable values MUST produce an explicit unavailable field.

#### Scenario: Request relative or absolute RBP fields
- **WHEN** the client requests explicit relative or absolute RBP field identifiers for one or more bands
- **THEN** the response contains only those requested fields with each source unit and RBP result identity preserved
- **AND** the request does not use `power_type` or `band_id`

#### Scenario: Request one PSD band-mean-density field
- **WHEN** the client requests a PSD field identifier for one canonical band in the bound result
- **THEN** the response contains only the band-mean density values for that band, exact frequency bounds, the same PSD window, and unit `V^2/Hz`
- **AND** the request does not start another PSD run or read raw recording samples

#### Scenario: Request a PSD frequency field
- **WHEN** the client requests a PSD field identifier for a bin present in the bound result
- **THEN** the response contains one field for that exact bin, its actual frequency, the same PSD window, and unit `V^2/Hz`
- **AND** the request does not start another PSD run or read raw recording samples

#### Scenario: Request an absent or invalid field
- **WHEN** a requested PSD band identifier is unsupported or its frequency range is not covered by the bound result
- **THEN** the API reports a structured invalid-field or unavailable-field result
- **AND** it does not choose a different band, clip the band silently, or use an older result

### Requirement: Algorithms register data adapters instead of drawing maps
Algorithms that support topomaps MUST register an adapter that identifies the source result, stable scalar field identifiers, units, window identity, channel order, quality, reference semantics, and spatial eligibility. An adapter MUST project values already present in the authoritative algorithm result; it MUST NOT implement WPF drawing, duplicate coordinate resolution, reread raw recording samples, or recompute the source algorithm. One algorithm MAY expose multiple fields, but every field MUST identify its value meaning and version.

#### Scenario: RBP registers band outputs
- **WHEN** RBP exposes per-channel relative or absolute power for Delta, Theta, Alpha, Beta, or Gamma
- **THEN** its adapter identifies each requested power/band field and reads only the matching structured RBP result array
- **AND** it does not derive absolute power from relative shares or diagnostic evidence

#### Scenario: PSD registers a band-mean-density field
- **WHEN** PSD exposes its current ordered frequency axis and channel-resolved result
- **THEN** its adapter integrates the selected canonical band from the current PSD density, divides by the complete band width, and returns one spatial field with the exact frequency range and `V^2/Hz` unit
- **AND** it shares the same interpolation and position path used by RBP without reading RBP result arrays

#### Scenario: PSD registers a frequency-bin field
- **WHEN** PSD exposes its current ordered frequency axis and channel-resolved result
- **THEN** its adapter registers the selected bin as one spatial field with the actual bin frequency and `V^2/Hz` unit
- **AND** it shares the same interpolation and position path used by RBP
