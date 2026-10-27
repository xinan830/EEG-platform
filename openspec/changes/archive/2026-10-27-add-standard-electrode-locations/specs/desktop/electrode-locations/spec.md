## Purpose

Preserve an explicit, stable scalp-site mapping for configured EEG input electrodes so future spatial displays can place values using recorded configuration metadata instead of guessing from channel text.

## ADDED Requirements

### Requirement: Channel settings disclose standard electrode locations
The system SHALL resolve recognized electrode labels by exact case-insensitive matching after trimming outer whitespace against MNE `standard_1020`. It SHALL return XYZ in meters, coordinate frame `mri`, the MNE version, and source identifier. Results SHALL be identified as standard-template locations, not measured cap-specific or patient-specific sensor positions. Unsupported labels SHALL be explicitly unavailable; the system MUST NOT infer or alias nearby sites. Resolution SHALL use a typed read-only backend API and keep MNE objects internal to the backend.

#### Scenario: Recognized electrode label
- **WHEN** a channel configuration contains a supported 10–20 electrode label
- **THEN** channel settings show its canonical scalp site and standard location status

#### Scenario: Unsupported or non-electrode label
- **WHEN** a channel label is unsupported, blank, or names a reference/ground rather than a scalp electrode
- **THEN** its scalp location is shown as unavailable and is excluded from spatial consumers

#### Scenario: Alias-like legacy label
- **WHEN** a channel uses a legacy name such as T3 and the operator expects T7
- **THEN** the resolver returns only the exact T3 site if present in the selected montage and never silently substitutes T7

#### Scenario: View resolved sites on a head map
- **WHEN** the user opens channel settings with resolved standard-template locations
- **THEN** the page shows those sites on a head-top illustration with the anterior direction at the top, labels each site, and identifies the positions as standard-template estimates rather than measured placement
- **AND** unresolved, reference, ground, or invalid snapshots are not assigned an invented map position

#### Scenario: Place a custom channel in a floating diagram
- **WHEN** the user opens the modeless electrode-map window, selects a configured custom EEG label, and clicks or drags within the head outline
- **THEN** the diagram shows and saves a separate normalized 2D manual position with the channel configuration
- **AND** the floating window does not block editing the channel settings
- **AND** the manual position is identified as diagram-only and is never stored as an MNE or measured XYZ location
- **AND** changing the channel label clears its previous manual position

#### Scenario: Hide a channel from the position diagram
- **WHEN** a configured EEG channel has a standard or manual position but its display toggle is off
- **THEN** the channel remains selectable for editing or placement
- **AND** its position is omitted from the electrode diagram until display is enabled again

### Requirement: Electrode locations are preserved with configuration identity
The system SHALL persist the resolved site identity, coordinate values, coordinate convention, and coordinate-source version with the channel configuration entry and any recording snapshot that carries that configuration. These fields SHALL participate in the channel configuration fingerprint. Older snapshots without location fields SHALL remain readable and SHALL resolve to unavailable; loading them SHALL NOT trigger resolution.

#### Scenario: Save and reload channel configuration
- **WHEN** a user saves a configuration containing recognized electrode labels and reloads it
- **THEN** the same site identities and coordinates are restored with the same coordinate convention and source version

#### Scenario: Historical snapshot after active profile edit
- **WHEN** the active channel profile changes after a recording snapshot was created
- **THEN** the historical snapshot retains its original electrode locations and fingerprint

#### Scenario: Legacy snapshot without locations
- **WHEN** the system opens a profile or recording snapshot created before spatial fields existed
- **THEN** it remains usable for existing acquisition/review behavior and reports location unavailable without fabricating coordinates

### Requirement: Channel location validation rejects contradictory mappings
The system SHALL validate that a stored standard site agrees with the configured electrode identity and declared source, frame, unit, version, finite-coordinate contract. Authoritative coordinates SHALL come from the backend resolver; the desktop SHALL NOT maintain a duplicate coordinate table. It SHALL reject duplicate assignment of one physical scalp site to multiple configured EEG inputs unless the profile explicitly represents separate inputs at the same site; blank and unsupported positions SHALL remain valid unavailable states for non-spatial use.

#### Scenario: Malformed or contradictory location snapshot
- **WHEN** a saved entry claims a site inconsistent with its channel label, has non-finite coordinates, or declares an unsupported frame, unit, or source
- **THEN** configuration validation fails with an actionable location error

#### Scenario: Duplicate scalp site
- **WHEN** two EEG inputs map to the same canonical scalp site without an explicit duplicate-site allowance
- **THEN** configuration validation identifies the duplicate site and blocks saving

### Requirement: Spatial metadata does not imply electrical values
The system SHALL treat scalp locations as display metadata only. It MUST NOT treat a bipolar derivation as the potential at either endpoint, derive an electrode potential from a bipolar value, or alter signal/reference calculations based on location metadata.

#### Scenario: Bipolar derivation is used by a spatial consumer
- **WHEN** a downstream spatial display receives a bipolar montage output without an explicitly defined spatial-value rule
- **THEN** the output is marked spatially ineligible rather than assigned to one endpoint

#### Scenario: Location metadata changes
- **WHEN** a user edits electrode location metadata
- **THEN** raw samples, montage reference formulas, and scientific analysis values remain unchanged while configuration identity reflects the metadata change
