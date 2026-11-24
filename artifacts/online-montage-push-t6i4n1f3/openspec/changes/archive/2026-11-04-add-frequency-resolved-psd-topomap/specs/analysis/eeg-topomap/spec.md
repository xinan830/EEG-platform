## MODIFIED Requirements

### Requirement: Topomap input is an explicit windowed channel result
Topomap input MUST be one selected scalar spatial field from an explicit file-absolute-time algorithm result. It MUST include `recording_id`, result/window identity, `window_start_s`, `window_end_s`, original channel order, one value and quality state per channel, a stable field identity, an explicit unit, algorithm version, and the bound montage snapshot identity. A frequency-resolved source MUST additionally identify the selected actual frequency bin and its `frequency_hz`. Internal values MUST follow the platform scientific-unit contract; public fields MUST declare their unit. The topomap MUST NOT infer time, channel positions, field identity, or values from the display clock or frontend.

#### Scenario: Algorithm supplies a valid band result
- **WHEN** an algorithm provides one complete window of per-channel values for a band or other scalar metric
- **THEN** its adapter preserves the window identity, original channel order, field identity, unit, algorithm version, montage identity, and per-channel quality
- **AND** the spatial projection consumes only that field's values

#### Scenario: PSD supplies one actual frequency bin
- **WHEN** PSD supplies its ordered channel-by-frequency result and a consumer selects one returned frequency bin
- **THEN** the adapter selects that exact bin for every channel and reports its exact `frequency_hz` with unit `V^2/Hz`
- **AND** it does not integrate, average, interpolate, or substitute values across frequency bins

#### Scenario: Result has no usable numeric value
- **WHEN** a channel value is missing, non-finite, quality-invalid, or its result window is incomplete
- **THEN** that channel is marked unavailable and excluded from spatial interpolation
- **AND** the system does not substitute zero, a previous window, a neighboring channel, or a value from another frequency bin

### Requirement: Algorithms register data adapters instead of drawing maps
Algorithms that support topomaps MUST register an adapter that identifies the source result, stable scalar field identifiers, units, window identity, channel order, quality, reference semantics, and spatial eligibility. An adapter MUST project values already present in the authoritative algorithm result; it MUST NOT implement WPF drawing, duplicate coordinate resolution, reread raw recording samples, or recompute the source algorithm. One algorithm MAY expose multiple fields, but every field MUST identify its value meaning and version.

#### Scenario: RBP registers band outputs
- **WHEN** RBP exposes per-channel relative or absolute power for Delta, Theta, Alpha, Beta, or Gamma
- **THEN** its adapter identifies each requested power/band field and reads only the matching structured RBP result array
- **AND** it does not derive absolute power from relative shares or diagnostic evidence

#### Scenario: PSD registers a frequency-bin field
- **WHEN** PSD exposes its current ordered frequency axis and channel-resolved result
- **THEN** its adapter registers the selected bin as one spatial field with the actual bin frequency and `V^2/Hz` unit
- **AND** it shares the same interpolation and position path used by RBP

### Requirement: Topomap visuals are prepared outside the UI dispatcher
WPF topomap visuals MUST prepare grid bitmaps, color ranges, and contour geometry outside the UI dispatcher and freeze every WPF drawing resource published across threads. `OnRender` MUST NOT traverse grid values to generate pixels or calculate contours; it SHALL draw prepared resources, head/electrode geometry, and lightweight scale labels. When the selected field, actual frequency bin, or canvas size changes, the system MUST cancel obsolete preparation and publish only a result that still matches the active field, window, size, and generation.

#### Scenario: A topomap window updates during playback
- **WHEN** the current window, selected field/frequency bin, or canvas size changes
- **THEN** the bitmap and contours are prepared in the background and frozen before UI publication
- **AND** a late result from an older field, window, or generation is discarded

#### Scenario: A new visual preparation is superseded
- **WHEN** a new window or selected field arrives before old visual preparation completes
- **THEN** obsolete preparation is cancelled and cannot replace the current field
- **AND** drawing resources cross to the UI thread only after they are frozen

### Requirement: Playback updates preserve topomap visual instances
Topomap presentation items MUST retain stable identity for the active Review session. When a new result window arrives, the client SHALL update the selected field data on the existing visual item and MUST NOT recreate the whole control collection. Adding or removing a requested field MAY add or remove only the corresponding field item.

#### Scenario: A new topomap response arrives during playback
- **WHEN** the same analysis session receives the next result window for its active field selection
- **THEN** the existing topomap control instances are updated with the new window's field data
- **AND** the prior complete visual remains visible until the next frozen visual is ready

### Requirement: Select RBP topomap power representation
RBP topomap fields SHALL identify both power representation and band, using the current structured relative or absolute result arrays. Relative fields SHALL declare unit `ratio`; absolute fields SHALL declare unit `uV^2`. Both SHALL use the same RBP window, recording, result identity, montage snapshot, channel order, spatial eligibility, and interpolation contract. The client MUST request explicit field identifiers and MUST NOT derive absolute power from relative shares or diagnostic evidence. An absolute field absent from a stored result SHALL be unavailable; it MUST NOT be reconstructed from another source.

#### Scenario: User selects absolute power
- **WHEN** the user selects an absolute-power field and the current result contains its structured absolute array
- **THEN** the response uses that exact array and declares unit `uV^2`
- **AND** it preserves the same window and montage identity as the relative field

#### Scenario: User selects relative power
- **WHEN** the user selects a relative-power field
- **THEN** the response uses the matching structured ratio array and declares unit `ratio`
- **AND** it uses the same result window and montage snapshot as an absolute field

#### Scenario: Absolute output is absent from a stored result
- **WHEN** absolute power is requested for a result without the structured array
- **THEN** that field is returned as unavailable with a stable reason
- **AND** no diagnostic evidence, other window, or other montage is used as a substitute

## REMOVED Requirements

### Requirement: Limit topomap interpolation to the selected frequency band
**Reason**: The band-only `band_id` request and `bands` response are replaced by the algorithm-neutral scalar `field_ids` and `fields` contract, which also represents a single PSD frequency bin.
**Migration**: All current topomap clients SHALL send explicit RBP field identifiers or a PSD frequency-bin field identifier and read the corresponding entries from `fields`. No fallback to `band_id`, `power_type`, or `bands` is retained.

## ADDED Requirements

### Requirement: Topomap transport uses explicit scalar field identifiers
The current topomap request MUST name one or more unique `field_ids` bound to a current result identity. The response MUST return those scalar spatial fields under `fields`, with each field's identifier, label, unit, field-specific metadata, per-channel points, quality, grid, and provenance. Frequency fields MUST report the exact selected `frequency_hz`. The current transport MUST NOT use the former `bands`, `power_type`, or `band_id` contract, infer a default field when `field_ids` are absent, or silently map an unknown field to another field. Unsupported identifiers MUST produce a structured invalid-request response; a known field with no usable values MUST produce an explicit unavailable field.

#### Scenario: Request relative or absolute RBP fields
- **WHEN** the client requests explicit relative or absolute RBP field identifiers for one or more bands
- **THEN** the response contains only those requested fields with each source unit and RBP result identity preserved
- **AND** the request does not use `power_type` or `band_id`

#### Scenario: Request a PSD frequency field
- **WHEN** the client requests a PSD field identifier for a bin present in the bound result
- **THEN** the response contains one field for that exact bin, its actual frequency, the same PSD window, and unit `V^2/Hz`
- **AND** the request does not start another PSD run or read raw recording samples

#### Scenario: Request an absent or invalid field
- **WHEN** a requested frequency bin is not present in the bound result or a field identifier is unsupported
- **THEN** the API reports a structured invalid-field error
- **AND** it does not choose the nearest frequency, a default band, or an older result
