## ADDED Requirements

### Requirement: PSD Review displays concise selected or all-band topomaps
PSD Review SHALL offer the five canonical PSD bands with concise display names and an “全部频段” option. Selecting one band SHALL display that band's map; selecting all bands SHALL request and display Delta, Theta, Alpha, Beta, and Gamma fields from the same PSD result window. Every response field SHALL preserve its exact frequency range, unit, channel order, quality, and field-specific provenance. Display labels MAY omit frequency bounds, but MUST NOT change the calculation or returned metadata.

#### Scenario: Display one PSD band with a concise label
- **WHEN** the user selects “Alpha 波” in the PSD topomap selector
- **THEN** Review requests `psd.band-mean-density.alpha` for the current dynamic PSD window
- **AND** the map title displays “Alpha 波” while the field retains the exact 8–13 Hz range and `V^2/Hz` unit

#### Scenario: Display all five PSD bands together
- **WHEN** the user selects “全部频段”
- **THEN** Review requests all five canonical `psd.band-mean-density` fields in one request for the current dynamic PSD window
- **AND** it displays five maps in canonical Delta, Theta, Alpha, Beta, Gamma order
- **AND** each map retains its own exact frequency range, unit, channel values, quality, and provenance

#### Scenario: One requested PSD field is invalid or unavailable
- **WHEN** a requested field identifier is unsupported or its frequency range is not fully covered by the PSD frequency axis
- **THEN** the backend rejects an invalid request or marks the affected field unavailable with a structured reason
- **AND** it does not substitute a different band, frequency range, window, or channel value

### Requirement: Topomap colors preserve the actual display range across units
Topomap rendering SHALL normalize each finite value using its actual nonzero display range without imposing a fixed absolute epsilon. Equivalent grids and proportional display bounds SHALL produce the same colors within pixel rounding tolerance, including `V^2/Hz` PSD density and `uV^2` absolute power. The existing scale-lock lifetime SHALL be preserved. Time, channel order, units, source values, quality, provenance, and failure semantics SHALL remain unchanged.

#### Scenario: Render a low-magnitude PSD density
- **WHEN** a PSD density grid has a finite, nonzero display range smaller than `1e-12 V^2/Hz`
- **THEN** its lower and upper bounds map to the color endpoints
- **AND** intermediate values use their proportional positions across the full color scale
- **AND** out-of-range values saturate at the respective endpoint colors

#### Scenario: Render a constant field
- **WHEN** the first ready field has equal finite minimum and maximum values
- **THEN** the legend preserves those exact equal bounds without an invented numeric expansion
- **AND** equal-valued cells use the midpoint color without contour lines
- **AND** later values below or above those locked bounds saturate at the respective endpoint colors

### Requirement: Average-referenced outputs retain their active electrode location
An acquisition-montage output with a validated `mean` reference formula SHALL use the formula's positive electrode location for spatial projection. Outputs with `channel` or `specifiedpair` reference formulas SHALL remain spatially unavailable unless a distinct spatial rule is explicitly defined; the topomap MUST NOT assign them to an arbitrary endpoint.

#### Scenario: Project an average-referenced channel
- **WHEN** PSD or RBP provides an output such as `F3-AVG` whose validated formula identifies `F3` as its positive electrode and `mean` as its reference kind
- **THEN** the topomap uses the saved position for `F3` and includes that result value when quality permits

#### Scenario: Keep a bipolar channel spatially unavailable
- **WHEN** PSD or RBP provides a `channel` or `specifiedpair` formula without an explicit spatial rule
- **THEN** the output remains excluded with a structured spatial-ineligibility reason
