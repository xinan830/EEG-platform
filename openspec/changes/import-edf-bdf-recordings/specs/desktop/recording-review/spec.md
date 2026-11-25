# Spec Delta

## MODIFIED Requirements

### Requirement: Project recordings open in an immersive review workspace
The desktop SHALL expose a review action for a selected recording under its owning project. The action SHALL open a full-content review workspace without the global navigation and SHALL retain enough project context to return to the same project recording list. Review SHALL open the exact selected source through its format-specific access path.

#### Scenario: A user opens a completed project recording
- **WHEN** the user invokes `回溯` for a readable WPF recording
- **THEN** the desktop SHALL load that exact recording directory
- **AND** it SHALL show the recording identity, absolute position, duration, sampling rate, and signal-channel facts from the recording
- **AND** returning SHALL restore the owning project selection

#### Scenario: A user opens an imported EDF or BDF recording
- **WHEN** the user invokes `回溯` for a readable imported EDF or BDF recording
- **THEN** the desktop SHALL load the registered source by its immutable recording identity using bounded sample reads
- **AND** it SHALL show the file-relative position, duration, sampling rate, and source-ordered channel facts
- **AND** returning SHALL restore the owning project selection

### Requirement: Review reads immutable raw chunks by bounded time window
The desktop SHALL read the manifest channel table and sample-major float64/V chunks without rewriting them or loading the complete recording into memory. Review time SHALL be derived from sample counter and sampling rate. Recorded gaps SHALL remain explicit and SHALL NOT be filled with fabricated samples.

#### Scenario: A requested window intersects a recorded gap
- **WHEN** the review reader loads a time range spanning a sample-counter gap
- **THEN** it SHALL return separate contiguous segments around the gap
- **AND** the renderer SHALL not connect those segments with a line
- **AND** the reader SHALL not insert zeros or interpolated samples.

#### Scenario: A chunk is truncated
- **WHEN** a batch header declares more payload bytes than remain in its chunk
- **THEN** review SHALL fail that read with an explicit corrupt-recording error
- **AND** it SHALL not display partial values as a complete window.

#### Scenario: Read an imported EDF/BDF window
- **WHEN** review requests a bounded time window from an imported EDF or BDF source
- **THEN** the source reader SHALL return ordered V/float64 channel samples and the corresponding file-relative sample range without loading the full recording
- **AND** any declared discontinuity SHALL remain explicit and SHALL NOT be bridged or filled with fabricated samples

### Requirement: Imported recordings use the shared offline algorithm panels
The desktop SHALL allow supported offline algorithms to run on imported EDF/BDF recordings through the existing backend algorithm APIs, using the imported recording's immutable backend recording ID and the same scientific definitions, parameter validation, and result presentation as WPF recordings. Registration metadata SHALL be source-neutral; imported recordings SHALL NOT require a WPF recording directory. Only validated imported EEG channel and montage provenance MAY be offered as algorithm inputs.

#### Scenario: Run an algorithm on an imported recording
- **WHEN** a user opens an available offline algorithm for an imported recording with valid metadata
- **THEN** the desktop SHALL build the same typed algorithm configuration used for WPF recordings
- **AND** it SHALL submit bounded-window requests using the backend recording ID
- **AND** it SHALL display the backend result using the existing algorithm panel

#### Scenario: Imported recording lacks required algorithm inputs
- **WHEN** the selected algorithm's required channels, montage, duration, or metadata are unavailable
- **THEN** the desktop SHALL expose a structured unavailable reason from the backend or input resolver
- **AND** it SHALL NOT submit a fabricated, zero-filled, or silently remapped request
