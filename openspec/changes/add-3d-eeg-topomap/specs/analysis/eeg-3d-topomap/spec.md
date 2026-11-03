## Purpose

Defines a reusable, traceable path for displaying existing per-channel EEG results on a registered three-dimensional head surface. The map is a spatial visualization of algorithm output and does not create or modify EEG metrics.

## ADDED Requirements

### Requirement: 3D electrode coordinates use a validated standard mapping
The 3D topomap SHALL resolve exact canonical sites from MNE `colin27_1020` through a versioned mapping to a named GNM model. The mapping SHALL identify its source montage and coordinate frame/unit, any required explicit transform, the target model and mesh identity, and the target coordinate frame/unit. The system MUST NOT infer a transform from coordinate values or use manual `MapX`/`MapY` as 3D coordinates.

#### Scenario: A supported standard site maps to the registered model
- **WHEN** an exact supported electrode label has a valid source site and matching GNM mapping/model assets
- **THEN** the 3D result contains the mapped model coordinate and mapping/model provenance

#### Scenario: A source, mapping, or model identity does not match
- **WHEN** a required source coordinate frame, unit, mapping identity, or mesh identity is missing or inconsistent
- **THEN** the 3D result is structurally unavailable with a stable reason and does not fall back to 2D or approximate coordinates

#### Scenario: A channel has no scalp site
- **WHEN** a channel is unsupported, derived, duplicated spatially, or is REF/GND
- **THEN** it is excluded with a structured reason and receives no invented 3D location

### Requirement: 3D topomaps consume existing algorithm results
The 3D topomap SHALL consume existing per-channel values and their declared channel order, unit, quality, algorithm identity, recording identity, and absolute time window. It MUST NOT rerun EEG preprocessing or algorithm formulas. Missing or non-finite values SHALL remain unavailable and MUST NOT be replaced with zero.

#### Scenario: RBP provides a ready five-band window
- **WHEN** an RBP result contains its current per-channel Delta, Theta, Alpha, Beta, and Gamma values
- **THEN** the 3D topomap uses those values and units for the corresponding bands without another RBP execution

#### Scenario: A channel value is absent or invalid
- **WHEN** a channel's value is missing, non-finite, or has no valid mapped site
- **THEN** that channel is excluded from the surface field with its reason while other valid channels remain usable

#### Scenario: A mapped mastoid location is used only as a display marker
- **WHEN** A1, A2, M1, or M2 has a mapped GNM coordinate
- **THEN** the shared model exposes that coordinate with `is_scalp_field_site=false` for Channel Settings markers
- **AND** the RBP scalp field continues to exclude it as a non-scalp interpolation site

### Requirement: The backend publishes a bounded, reproducible surface field
The backend SHALL project valid channel values onto the declared GNM scalp region with a deterministic, versioned spatial method. Each response SHALL identify the method and parameters, preserve exact values at mapped electrode sites, identify its mesh vertex ordering, and leave unsupported surface locations unavailable rather than extrapolating beyond the declared scalp region.

#### Scenario: A surface field is produced
- **WHEN** a band has enough valid mapped channel values for the declared method
- **THEN** the backend returns a surface field aligned to the identified mesh and records the method, parameters, and contributing channels

#### Scenario: The surface field cannot be produced
- **WHEN** valid points are insufficient, conflicting, or outside the declared scalp region
- **THEN** the result reports a structured unavailable status and does not show a fabricated complete surface

### Requirement: Model and mapping provenance are verifiable
Every 3D result SHALL include the mapping identifier/version and content identity, GNM model identifier/version and mesh identity, source montage/version, coordinate frames/units, algorithm versions, recording identity, absolute window, band units, and included/excluded channel reasons. The 3D view SHALL identify the result as a standard-template visualization, not measured cap placement or clinical localization.

#### Scenario: A user inspects a 3D result
- **WHEN** the 3D head surface is displayed
- **THEN** its band, unit, absolute result window, standard-template status, and source algorithm are available for inspection

### Requirement: RBP has an independent 3D presentation
The RBP workspace SHALL allow viewing one selected band on the reusable GNM 3D surface while keeping the existing 2D topomap separate and available. Changing the 3D band SHALL change only the displayed band, not its algorithm parameters or calculated values.

#### Scenario: A user selects an RBP band
- **WHEN** the user selects Delta, Theta, Alpha, Beta, or Gamma in the 3D view
- **THEN** the same 3D model displays that band's current window and scale without rebuilding the EEG result

### Requirement: 3D playback updates publish complete current-window visuals
The 3D view SHALL follow the review workspace's current absolute analysis window, reject superseded results, and publish a complete prepared visual state. Model loading, surface preparation, and scene updates MUST NOT block waveform playback or perform EEG computation on the UI dispatcher. The static model SHALL be reused while its verified identity remains unchanged.

#### Scenario: A newer playback window supersedes a pending result
- **WHEN** a prior window's result or scene preparation completes after the review cursor has advanced
- **THEN** the prior result is discarded and cannot replace the current 3D surface

#### Scenario: A new window arrives during playback
- **WHEN** the current 3D surface is being updated
- **THEN** the view keeps the last complete surface until a complete current-window surface is ready, without pausing waveform playback
