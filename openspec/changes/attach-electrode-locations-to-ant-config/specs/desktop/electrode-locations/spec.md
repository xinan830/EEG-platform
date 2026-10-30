## MODIFIED Requirements

### Requirement: Channel settings disclose standard electrode locations
The system SHALL resolve recognized electrode labels by exact case-insensitive matching after trimming outer whitespace against MNE `colin27_1020`. It SHALL return XYZ in meters, coordinate frame `mri`, the MNE version, and source identifier. Results SHALL be identified as standard-template locations, not measured cap-specific or patient-specific sensor positions. Unsupported labels SHALL be explicitly unavailable; the system MUST NOT infer or alias nearby sites. Resolution SHALL use a typed read-only backend API and keep MNE objects internal to the backend. The backend SHALL derive 2D head-map positions from MNE's public `mne.viz.plot_sensors(kind="topomap", sphere="eeglab")` sensor projection and return those positions with the XYZ snapshot; the desktop SHALL consume returned 2D positions and MUST NOT calculate a second projection from XYZ. Map coordinates SHALL orient anterior sites upward and posterior sites downward in the diagram, fit the standard 10–20 perimeter to 0.92 of the diagram radius, and constrain peripheral template points to that radius without changing their returned XYZ. When the verified ANT default system channel configuration is generated, the desktop SHALL resolve its named scalp electrodes using this API and attach successful location snapshots to corresponding channel entries; REF and GND remain separate landmarks, and unresolved positions remain unavailable. A legacy snapshot without MNE topomap coordinates SHALL remain unprojected until explicitly resolved again; the desktop MUST NOT fabricate or backfill a position from XYZ.

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

#### Scenario: Use MNE's projected sensor positions
- **WHEN** a recognized standard electrode location is resolved for the map
- **THEN** the backend returns the MNE topomap sensor projection alongside its XYZ and MNE version
- **AND** the desktop draws the returned position without deriving map coordinates from XYZ
- **AND** occipital sites O1/O2/Oz appear posterior to Pz and near the back edge of the head diagram
- **AND** peripheral points remain within the diagram radius while the MNE XYZ coordinates remain unchanged
- **AND** older snapshots without returned map coordinates stay unavailable to the map until explicitly resolved again

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

#### Scenario: Resolve positions for the ANT system configuration
- **WHEN** the verified ANT default system configuration is loaded and the location service is available
- **THEN** every named scalp channel receives the exact supported MNE standard-template location, while unsupported labels remain unavailable
- **AND** REF and GND are not treated as scalp electrodes

#### Scenario: Location service unavailable for ANT configuration
- **WHEN** the ANT system configuration is loaded while the location service is unavailable or fails
- **THEN** the channel configuration remains usable with positions unavailable and the user is informed that spatial positions could not be resolved
- **AND** no coordinates are fabricated or substituted from a desktop-maintained table
