# analysis/dynamic-spectral-results-ui Specification

## Purpose
TBD - created by archiving change integrate-dynamic-spectral-results-ui. Update Purpose after archive.
## Requirements
### Requirement: Expose bounded backend-produced structured previews

The backend SHALL expose a checksum-verified, bounded preview for structured
PSD/STFT Run artifacts. The preview SHALL include output kind, axes, units,
window states, artifact identity, and null values for unavailable cells. It
SHALL NOT require frontend scientific recomputation.

#### Scenario: Read a dynamic STFT preview

- **WHEN** the client requests a valid completed dynamic STFT Run
- **THEN** the response contains bounded backend-produced time-frequency values,
  seconds/Hz axes, declared display units, window state rows, and artifact
  identity

#### Scenario: Artifact integrity failure

- **WHEN** the artifact checksum does not match its repository metadata
- **THEN** the endpoint returns a structured integrity error and no values

### Requirement: Render structured spectral results without changing science

The frontend SHALL render backend-returned structured PSD/STFT previews using
their declared units and axes. It SHALL show unavailable cells as blank or
neutral values and SHALL NOT convert units, classify quality, or downsample
scientific arrays in browser code.

#### Scenario: Rejected dynamic window

- **WHEN** a preview contains a `Rejected` or `Unavailable` window
- **THEN** the result view labels the state and does not display its null cells as
  zero power

### Requirement: Preserve existing result views

Adding structured previews SHALL NOT remove or alter scalar metric, legacy
spectrum, spectrogram, provenance, or export rendering for existing Runs.

#### Scenario: Existing scalar Run

- **WHEN** the result workbench opens an existing scalar official Run
- **THEN** it continues to render the current scalar result and export controls

