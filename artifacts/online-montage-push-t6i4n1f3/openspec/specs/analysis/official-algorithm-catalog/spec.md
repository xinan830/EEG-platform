# analysis/official-algorithm-catalog Specification

## Purpose
TBD - created by archiving change modularize-official-algorithm-boundaries. Update Purpose after archive.
## Requirements
### Requirement: Backend exposes an authoritative official-algorithm catalog

The official catalog SHALL derive runnable official metadata from the installed
runtime module manifest, including Definition identity, scientific version,
implementation identity, output schema, parameters, modes, strategy
capabilities and availability. A non-runnable capability MAY use an explicit
static descriptor only when that descriptor is declared by the authoritative
catalog source. A missing runtime module for an executable manifest or any
catalog/runtime identity mismatch SHALL fail the official catalog response;
the API SHALL NOT synthesize parameters, policy, availability, or capability
values. The active scientific version SHALL be explicitly declared for each
official algorithm ID; catalog clients SHALL NOT select retired versions.

#### Scenario: Read the catalog

- **WHEN** a client requests `/api/algorithms`
- **THEN** it can distinguish official from user algorithms and render each
  selected algorithm's inputs without inspecting internal DAG or adapter data
- **AND** each official algorithm ID exposes only its explicitly active version

#### Scenario: All installed official algorithms are listed

- **WHEN** a client requests `GET /api/algorithms`
- **THEN** the response lists every installed platform official algorithm
- **AND** executable items report their runtime-owned runnable state without
  an installed Definition prerequisite
- **AND** an explicit descriptor for a capability without an installed executor is not represented as runnable

#### Scenario: Installed evidence is unavailable

- **WHEN** an executable manifest cannot resolve to its current runtime module,
  or catalog and runtime identity/contracts disagree
- **THEN** the response returns `OFFICIAL_ALGORITHM_CATALOG_UNAVAILABLE`
- **AND** no synthetic partial official item is returned with empty parameters
  or a hard-coded dynamic policy

#### Scenario: Read catalog after RBP and FAA enablement

- **WHEN** a client requests `/api/algorithms` with validated RBP, FAA and offline Brainbeat modules installed
- **THEN** those entries are available and runnable with readable Chinese labels and declared input fields
- **AND** offline Brainbeat does not advertise a stateful realtime execution mode

#### Scenario: Read a runnable official catalog item

- **WHEN** a client reads `/api/algorithms`
- **THEN** each runnable official item exposes the same identity and version that will be persisted and executed for a Run
- **AND** the client does not infer identity from the algorithm name or a duplicated catalog mapping

#### Scenario: Retired version is not selectable

- **WHEN** a client requests the catalog after an official algorithm advances to a new current version
- **THEN** the catalog exposes the declared current version only
- **AND** it does not advertise or select a retired calculator version for new Runs

### Requirement: Official algorithm availability is not inferred by clients

The frontend SHALL render official algorithm label, purpose, status, and disabled state from the catalog response. It SHALL NOT derive them from an algorithm name, graph, quality rules, or Definition owner field.

#### Scenario: Catalog failure does not hide user algorithms

- **WHEN** the official catalog request fails while the user Definition request succeeds
- **THEN** the user algorithm list remains available
- **AND THEN** the official section presents a scoped catalog failure message.

### Requirement: Official algorithm refactoring preserves frozen contracts

The system SHALL keep each official algorithm's scientific calculation in its
own module and SHALL preserve that current module's numerical, unit,
channel-order, quality, null, and timing semantics during management-layer
changes. It SHALL NOT add compatibility adapters for retired scientific
versions as part of algorithm management.

#### Scenario: Missing required channel role

- **WHEN** an official algorithm requires Fz/Pz/Oz or F3/F4 roles and the caller does not provide an explicit mapping
- **THEN** it is unavailable with a structured reason
- **AND THEN** the system does not infer Oz from O2 or from channel order.

#### Scenario: Consolidate management metadata

- **WHEN** common catalog or registration metadata is consolidated
- **THEN** each algorithm's formula and algorithm-specific scientific policy remain owned by its module
- **AND** numerical, unit, channel-order, quality, unavailable-value, and timing behavior remains unchanged

### Requirement: Keep non-runnable official algorithms unavailable

The catalog SHALL keep capabilities without validated executable modules
non-runnable. It SHALL NOT disable installed validated FAA or offline
Brainbeat modules merely because an earlier rollout left them disabled.
IAPF and Theta/Beta v2 SHALL expose their own raw channel parameter schemas
and SHALL NOT declare required global roles.

#### Scenario: Read Theta/Beta catalog metadata

- **WHEN** a client reads the Theta/Beta catalog entry
- **THEN** it describes one selected raw channel and contains no Fz/Pz/Oz mapping prerequisite

#### Scenario: Read offline Brainbeat catalog metadata

- **WHEN** the validated offline Brainbeat module is installed
- **THEN** its entry is runnable with explicit distinct Fz and Pz source inputs
- **AND** its supported modes remain static and dynamic offline execution

#### Scenario: Reject incomplete management registration

- **WHEN** an official module is missing required catalog metadata or its declared desktop integration
- **THEN** the release completeness check fails with the missing registration points identified
- **AND** scientific execution remains unavailable through an incomplete catalog entry

### Requirement: Official PSD publishes ordered channel-resolved spectra
The current official PSD contract SHALL accept a nonempty, unique, explicitly ordered set of analysis channels and SHALL calculate each channel with the same declared Welch, filter, window, and preprocessing configuration. Results SHALL expose the exact requested `channel_order` and one shared, strictly increasing `frequency_hz` axis. Static `psd` values SHALL have shape `[channel, frequency]`; dynamic values SHALL have shape `[window, channel, frequency]`. Derived `band_share` values SHALL preserve the corresponding channel and window axes. Each channel in each window SHALL have its own quality state and unavailable reason. A rejected or unavailable channel SHALL NOT invalidate valid channels in the same window, and unavailable values SHALL NOT be replaced by zero, a neighboring channel, or a previous window.

#### Scenario: Run PSD for an ordered channel set
- **WHEN** a static or dynamic PSD request supplies a unique ordered channel set
- **THEN** the result returns the same channel order, one shared frequency axis, and the declared static or dynamic array shape
- **AND** each channel uses the same scientific configuration and actual window boundaries

#### Scenario: One channel fails its quality gate
- **WHEN** one channel fails the PSD quality gate while other requested channels pass for the same dynamic window
- **THEN** the failed channel has a channel-local rejected state and reason
- **AND** the other channels retain their valid PSD values and states

#### Scenario: Requested channel set is invalid
- **WHEN** a PSD request contains a duplicate, unknown, or empty channel set
- **THEN** the request fails with a structured input error
- **AND** the runtime does not reorder channels or guess missing channels

#### Scenario: Channel frequency axes disagree
- **WHEN** channel calculations for one result do not produce the same declared frequency axis
- **THEN** PSD execution fails with a structured contract error
- **AND** the result does not publish a misaligned matrix

### Requirement: Standardize shared band integration without flattening algorithm parameters
The system SHALL use the shared band-power integration contract for PSD band shares and offline BrainBeat band powers. The integration SHALL interpolate PSD values at exact band edges and use trapezoidal integration. Standardizing integration SHALL NOT force algorithms to share frequency bands, denominators, filter settings, or Welch window lengths. Classic BrainBeat SHALL integrate fixed 4–8 Hz Fz and 8–12 Hz Pz absolute band powers and divide them without broadband normalization. `brainbeat_individualized` SHALL retain its IAPF-derived Theta and Alpha ranges and 1–30 Hz per-channel denominators. Both variants' persisted spectral metadata SHALL match the four-second Welch segment used by the shared spectral source.

#### Scenario: Compare band power under identical parameters
- **WHEN** PSD and Brainbeat integrate the same PSD values over the same band
- **THEN** both return the same boundary-interpolated trapezoidal result within floating-point tolerance
- **AND** each algorithm retains its own declared bands and normalization semantics

#### Scenario: Brainbeat Welch metadata matches execution
- **WHEN** a current Brainbeat Run stores its spectral definition
- **THEN** the persisted Welch segment length is four seconds and overlap is 50 percent
- **AND** the samples, filters, and selected variant's window policy match the execution

### Requirement: Expose classic and individualized BrainBeat as distinct official algorithms

The official catalog SHALL expose classic `brainbeat` and
`brainbeat_individualized` as separate runnable offline algorithm identities.
Classic SHALL declare fixed 4–8 Hz Fz Theta and 8–12 Hz Pz Alpha
absolute-power ratio semantics and SHALL NOT require IAPF. The individualized
entry SHALL declare its IAPF-relative relative-power formula. Both entries
SHALL expose explicit Fz/Pz roles, distinct scientific and implementation
identities, output units, quality contracts, and 20-second/one-second dynamic
defaults.

#### Scenario: Read both BrainBeat catalog entries
- **WHEN** a client requests the official algorithm catalog
- **THEN** it can distinguish the variants by ID, label, formula, IAPF dependency, and scientific version
- **AND** neither entry is synthesized from frontend defaults

#### Scenario: Select a current BrainBeat identity
- **WHEN** a new Run or dynamic review request selects one BrainBeat variant
- **THEN** only that variant's declared current scientific identity and configuration are used
- **AND** the formulas cannot be selected through an ambiguous shared label or implicit fallback

### Requirement: Current official algorithm parameters match executable defaults

For PSD, the official catalog parameter schema SHALL declare the same defaults as the current typed PSD configuration for `low_hz`, `high_hz`, and `notch_hz`. Catalog consumers may use these declared defaults to construct a current-version request without maintaining a second copy. Changing the catalog declaration alone SHALL NOT change the executable configuration defaults.

#### Scenario: Resolve the PSD catalog entry

- **WHEN** a consumer reads the current PSD catalog parameter schema
- **THEN** the defaults are `low_hz=1.0 Hz`, `high_hz=30.0 Hz`, and `notch_hz=50.0 Hz`
- **AND** each equals the corresponding current `PsdConfig` default.

### Requirement: PSD consumers use only the current structured band-share contract

PSD presentation consumers SHALL read band-share values from the current `band_share` result array and SHALL NOT reconstruct them from retired `spectral_evidence.relative_band_power` fields.

#### Scenario: Retired PSD evidence is present without a current band-share array

- **WHEN** a PSD result omits the current `band_share` array but contains retired `spectral_evidence.relative_band_power` evidence
- **THEN** PSD spectrum presentation may render validated current `frequency_hz` and `psd` data
- **AND** band-share presentation remains unavailable
- **AND** the retired evidence is not converted or presented as a current result.
