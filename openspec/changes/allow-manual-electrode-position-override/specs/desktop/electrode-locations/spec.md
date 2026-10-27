## MODIFIED Requirements

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

#### Scenario: Override a standard-template position in the diagram
- **WHEN** the user selects an EEG channel with a resolved standard-template location and clicks or drags within the head outline
- **THEN** the diagram uses and saves the normalized 2D manual position for that channel while retaining its resolved standard XYZ snapshot unchanged
- **AND** the override is identified as diagram-only and is never treated as measured or standard XYZ coordinates
- **AND** the manually overridden point is visually distinguishable from an unmodified standard-template point
- **AND** changing the diagram-only position does not change the signal mapping identity of the channel configuration

#### Scenario: Restore all default template positions
- **WHEN** the user activates restore all default template positions
- **THEN** every EEG channel's manual 2D override is cleared
- **AND** standard-template channels return to their resolved standard-template diagram positions without changing their XYZ snapshots
- **AND** custom channels without standard-template positions are omitted from the diagram

#### Scenario: Hide a channel from the position diagram
- **WHEN** a configured EEG channel has a standard or manual position but its display toggle is off
- **THEN** the channel remains selectable for editing or placement
- **AND** its position is omitted from the electrode diagram until display is enabled again

#### Scenario: Show and move hardware reference and ground landmarks
- **WHEN** the user opens the floating electrode map for a channel configuration
- **THEN** it shows separate REF and GND landmarks at the configured positions, defaulting to the Pz/Cz midpoint and Fz/Fpz midpoint
- **AND** the user can drag either landmark independently without selecting an EEG channel
- **AND** moving a landmark updates the profile's saved 2D diagram position
- **AND** the landmark positions are persisted as normalized diagram metadata with the profile
- **AND** these landmarks are not EEG channel rows, sample columns, MNE scalp snapshots, or signal-fingerprint inputs
- **AND** existing hardware REF/GND wiring labels and acquisition provenance remain unchanged
- **AND** restoring default positions returns the landmarks to the default diagram positions
