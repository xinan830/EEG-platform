## ADDED Requirements

### Requirement: Standard 3D electrode locations resolve through an explicit GNM mapping
The system SHALL provide a typed read-only 3D location resolution path for exact `colin27_1020` electrode labels. A mapped result SHALL include the authoritative MNE source identity and coordinates, mapping identity, target GNM model identity, target coordinates, explicit source/target frames and units, and an explicit `is_scalp_field_site` classification. Coordinate availability SHALL remain independent from eligibility for scalp-field interpolation. The existing 2D resolution and manual diagram positions SHALL remain independent. A missing or invalid 3D mapping SHALL return a structured unavailable result and MUST NOT fall back to `MapX`/`MapY` or an approximate mapping.

#### Scenario: Resolve a mapped electrode location
- **WHEN** a requested label exactly matches a site in the validated mapping and the model identities validate
- **THEN** the API returns its GNM coordinates and complete source/target provenance in request order
- **AND** reports whether that location is a scalp-field site independently from coordinate availability

#### Scenario: Resolve an unsupported or unmapped label
- **WHEN** a requested label is blank, unsupported, REF/GND, or absent from the mapping
- **THEN** its 3D location is unavailable with a reason and no coordinate is inferred

#### Scenario: Mapping assets are incompatible
- **WHEN** the source frame/unit cannot be related explicitly to the platform's MNE coordinates or the mapping references another GNM mesh
- **THEN** the API returns a stable structured error/unavailable status and leaves the existing 2D location behavior unchanged

### Requirement: Channel Settings can inspect mapped locations in 2D or 3D
The Channel Settings electrode-position window SHALL allow switching between its existing editable 2D diagram and a read-only 3D view of the shared GNM model. The 3D view SHALL use the verified standard model and mapping and SHALL show only currently enabled EEG inputs with exact mapped locations, including mapped A1/A2/M1/M2 when configured. Location resolution SHALL use the shared `is_scalp_field_site` classification; display availability MUST NOT be conflated with scalp-field eligibility. It MUST NOT derive 3D coordinates from editable `MapX`/`MapY`. REF/GND and unmapped labels SHALL remain unavailable in 3D with no inferred position. The camera target and rotation pivot SHALL share the normalized model origin. Zoom SHALL change the camera field of view while keeping the camera outside a bounding sphere enclosing the complete displayed model.

#### Scenario: Switch to the 3D electrode view
- **WHEN** the operator selects 3D in the Channel Settings electrode-position window
- **THEN** the shared head model is displayed with the mapped positions for currently enabled EEG inputs
- **AND** switching back to 2D preserves the existing editable diagram and draft positions

#### Scenario: A configured channel has no 3D scalp site
- **WHEN** an enabled EEG label is absent from the standard mapping
- **THEN** the 3D view reports that label as unavailable and does not place it from its 2D diagram position

#### Scenario: A mapped mastoid channel is enabled
- **WHEN** A1, A2, M1, or M2 is enabled as an EEG input and has an exact standard mapping
- **THEN** Channel Settings displays its mapped 3D location marker
- **AND** its scalp-field-site classification remains false

#### Scenario: Channel Settings uses mapped markers without floating labels
- **WHEN** the operator views mapped EEG inputs in Channel Settings 3D
- **THEN** each enabled mapped electrode is represented by a marker at its standard 3D location
- **AND** channel names remain available in the adjacent channel list
- **AND** the 3D view does not render floating channel-name labels or connector lines
- **AND** the marker coordinates and backend mapping remain unchanged

#### Scenario: The operator zooms toward the model
- **WHEN** the operator zooms in repeatedly
- **THEN** the view magnifies by narrowing the field of view while the camera remains outside the model's enclosing sphere
- **AND** the camera does not pass through the head surface at any supported zoom level

#### Scenario: The operator pans the model with Shift-drag
- **WHEN** the operator drags the pointer while holding Shift
- **THEN** the model follows the pointer in both horizontal and vertical screen directions

#### Scenario: The operator edits 2D positions or hardware landmarks
- **WHEN** the operator drags an EEG marker, REF, or GND in the 2D view
- **THEN** only the existing 2D draft position changes; the read-only 3D standard mapping remains unchanged

#### Scenario: A saved profile is previewed without a loaded device draft
- **WHEN** a saved channel profile is selected but no device draft is loaded
- **THEN** the position window shows its saved EEG channel labels and enabled states in both views
- **AND** the 2D positions remain read-only while the 3D view uses only the shared standard mapping
