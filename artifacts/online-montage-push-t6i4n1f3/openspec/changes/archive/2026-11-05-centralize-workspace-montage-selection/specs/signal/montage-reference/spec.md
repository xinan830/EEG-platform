## MODIFIED Requirements

### Requirement: Separate display montage from analysis reference

The system SHALL preserve acquisition montage, review-workspace montage selection, and immutable analysis provenance as distinct concepts. Within an active recording-review workspace, one selected montage snapshot SHALL be the shared source for waveform display and algorithm input mapping. Scientific requests SHALL include the exact selected montage identity and resolved input mapping; the analysis path SHALL NOT maintain an independent montage selection. Acquisition snapshots and completed historical Runs SHALL remain immutable.

#### Scenario: Select a montage in recording review
- **WHEN** the user changes the selected montage in a recording-review workspace
- **THEN** waveform display and algorithm inputs resolve through that same montage snapshot
- **AND** the scientific request and result provenance identify its exact identity and mapping

#### Scenario: Select an average workspace montage

- **WHEN** the user changes the recording-review workspace montage to average reference
- **THEN** waveform display and algorithm input mapping both use that average-reference snapshot
- **AND** any subsequent scientific request uses a new identity for the selected mapping

#### Scenario: Select an average display montage
- **WHEN** the user changes the workspace montage to average reference
- **THEN** both the viewer and algorithm input mapping use the average-reference snapshot
- **AND** a new scientific request identity is used for any subsequent Run

#### Scenario: Select a montage from an algorithm panel
- **WHEN** a user initiates a montage change while viewing an algorithm panel
- **THEN** the workspace's single montage selection changes
- **AND** waveform display and all algorithm panels observe that same selection

#### Scenario: Select an analysis montage in the algorithm detail
- **WHEN** the user changes the montage while an algorithm detail is active
- **THEN** the workspace selection changes to that validated immutable snapshot
- **AND** waveform display and every algorithm panel resolve inputs through it
- **AND** the algorithm request and provenance identify the selected snapshot

#### Scenario: Change montage while viewing a review algorithm panel

- **WHEN** the user changes the montage from an algorithm panel in recording review
- **THEN** the single workspace selection changes
- **AND** waveform display and every algorithm panel observe that same selection

#### Scenario: Preserve acquisition and historical provenance
- **WHEN** a review workspace selects a montage other than the acquisition montage
- **THEN** the raw recording and acquisition montage snapshot remain unchanged
- **AND** previously completed results retain their original montage provenance

#### Scenario: A selected montage cannot map a required input
- **WHEN** the workspace-selected montage cannot provide a required source or semantic channel
- **THEN** the operation reports a structured unavailable or validation reason
- **AND** it does not infer a different montage from waveform state or use positional substitution
