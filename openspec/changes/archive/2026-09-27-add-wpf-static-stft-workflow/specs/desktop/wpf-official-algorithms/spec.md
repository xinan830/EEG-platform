# desktop/wpf-official-algorithms Specification

## ADDED Requirements

### Requirement: WPF submits official static STFT with explicit raw inputs

WPF SHALL use a completed, backend-registered recording, one backend-declared raw EEG channel, an explicit finite in-record range of at least four seconds, and the catalog scientific version. It SHALL submit `mode=static` without changing the backend algorithm or raw recording. The dynamic mode SHALL remain unavailable in this form.

#### Scenario: Valid static STFT request

- **WHEN** the operator chooses STFT, a registered channel, and a valid requested start/end range
- **THEN** WPF sends those exact seconds and channel in an official-algorithm Run
- **AND** displays the backend Run identity, requested/actual range, quality, and result status

#### Scenario: Invalid range or channel

- **WHEN** the range is shorter than four seconds, nonfinite, outside the recording, or the channel is absent from the registration response
- **THEN** WPF refuses submission and shows a specific error

### Requirement: STFT preview preserves backend axes, units, quality, and missing cells

WPF SHALL render only a bounded backend-produced static `time x frequency` matrix. Time centers SHALL be labeled in seconds, frequency in Hz, and power in `dB re 1 uV^2/Hz`. WPF SHALL NOT derive spectral power or replace null/nonfinite cells with zero. Invalid shape/unit or a preview over the cell limit SHALL be reported as unavailable without claiming that the completed Run failed.

#### Scenario: A partial preview contains unavailable cells

- **WHEN** a completed Run returns a valid two-dimensional `power_db` matrix containing null cells
- **THEN** WPF leaves those cells visually distinct from numeric power and keeps the backend quality/provenance readable

#### Scenario: Preview exceeds the transport ceiling

- **WHEN** the backend rejects a requested 100,000-cell preview
- **THEN** WPF explains that the saved result is too large for this view and suggests a shorter analysis range
