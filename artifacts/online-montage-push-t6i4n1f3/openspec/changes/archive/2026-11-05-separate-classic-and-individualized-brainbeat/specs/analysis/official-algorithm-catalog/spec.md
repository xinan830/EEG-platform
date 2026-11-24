## ADDED Requirements

### Requirement: Expose classic and individualized BrainBeat as distinct official algorithms
The official catalog SHALL expose classic `brainbeat` and `brainbeat_individualized` as separate runnable offline algorithm identities. The classic entry SHALL declare fixed 4–8 Hz Fz Theta and 8–12 Hz Pz Alpha absolute-power ratio semantics and SHALL NOT require IAPF. The individualized entry SHALL declare its IAPF-relative relative-power formula. Both entries SHALL expose explicit Fz/Pz roles, their own scientific and implementation identities, output units, quality contract, and 20-second/one-second dynamic defaults.

#### Scenario: Read both BrainBeat catalog entries
- **WHEN** a client requests the official algorithm catalog
- **THEN** it can distinguish classic BrainBeat from the individualized variant by ID, label, formula, IAPF dependency, and scientific version
- **AND** neither entry is synthesized from frontend defaults

#### Scenario: Select a current BrainBeat identity
- **WHEN** a new Run or dynamic review request selects one BrainBeat variant
- **THEN** only that variant's declared current scientific identity and configuration are used
- **AND** the two formulas cannot be selected through an ambiguous shared label or implicit fallback

## MODIFIED Requirements

### Requirement: Standardize shared band integration without flattening algorithm parameters
The system SHALL use `scientific/primitives/spectral.py::band_power` for PSD band shares and offline BrainBeat band powers. The primitive SHALL interpolate PSD values at exact band edges and integrate with the trapezoidal rule. Standardizing integration SHALL NOT force algorithms to share frequency bands, denominators, filter settings, or Welch window lengths. Classic BrainBeat SHALL integrate fixed 4–8 Hz Fz and 8–12 Hz Pz absolute band powers and divide them without broadband normalization. `brainbeat_individualized` SHALL retain its IAPF-derived Theta and Alpha ranges and 1–30 Hz per-channel denominators. Both variants' persisted spectral metadata SHALL match the four-second Welch segment used by the shared spectral source.

#### Scenario: Compare band power under identical parameters
- **WHEN** PSD and either BrainBeat variant integrate the same PSD values over the same band
- **THEN** both return the same shared primitive result within floating-point tolerance
- **AND** each algorithm retains its own declared bands and normalization semantics

#### Scenario: Brainbeat Welch metadata matches execution
- **WHEN** a current BrainBeat Run stores its spectral definition
- **THEN** the persisted Welch segment length is four seconds and overlap is 50 percent
- **AND** the samples, filters, and selected variant's window policy match the execution
