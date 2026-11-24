## MODIFIED Requirements

### Requirement: Preserve review time across scientific configuration changes
Changing the applied PSD channel set or scientific parameters SHALL preserve review position and playback state while creating a new configuration revision. The PSD configuration SHALL capture an immutable ordered analysis channel set and montage identity. Display channel selection and PSD frequency-bin selection SHALL be presentation state over the current result and SHALL NOT create a new Run. Display montage and filtered display samples SHALL remain separate from scientific inputs. The workspace SHALL own shared filter values and analysis window settings; PSD SHALL compute from the configured analysis recording inputs using its declared preprocessing contract.

#### Scenario: Edit shared settings while a Run is being prepared
- **WHEN** shared filters or analysis window length change while old work is pending
- **THEN** old results and progress cannot be presented as the new configuration
- **AND** review position and playback state remain unchanged
- **AND** the user applies the new configuration explicitly

#### Scenario: Select the analysis window from the workspace menu
- **WHEN** the user opens Analysis / Analysis Interval / Analysis Window Length
- **THEN** choices and default come from the backend algorithm policy
- **AND** the PSD panel does not expose duplicate filter or window inputs

#### Scenario: Change channel while paused
- **WHEN** a user changes the PSD spectrum display channel while paused at 17 seconds
- **THEN** the review remains paused at 17 seconds
- **AND** the chart selects that channel from the current channel-resolved Run without submitting another Run
- **AND** unavailable channels are shown with their structured state rather than a substituted channel

#### Scenario: Change the applied PSD channel set
- **WHEN** the user applies a different analysis channel set while paused
- **THEN** the review position and playback state remain unchanged
- **AND** a new configuration revision uses the newly captured ordered set
- **AND** results from the prior configuration cannot replace it

## ADDED Requirements

### Requirement: Review maps a selected PSD frequency from the current result
PSD Review SHALL use one channel-resolved PSD Run for its spectrum and topomap. Its frequency selector SHALL list only bins present in that Run's `frequency_hz` axis. Selecting a frequency bin SHALL request its exact PSD values from the same result/window and bound montage snapshot, with unit `V^2/Hz`; it SHALL NOT start another PSD Run, reread raw recording samples, interpolate between frequency bins, or use RBP band power. Review's shared time coordinator SHALL select the current eligible PSD window and prevent an obsolete response from replacing a newer cursor/window identity.

#### Scenario: Select a frequency bin for the current PSD window
- **WHEN** the user selects a frequency bin returned by the active PSD Run
- **THEN** the topomap displays the per-channel PSD values from exactly that bin and window
- **AND** the response reports that bin's actual frequency and `V^2/Hz`
- **AND** the PSD Run identity and review playback state remain unchanged

#### Scenario: The selected frequency is unavailable in the current result
- **WHEN** a frequency-bin selection does not exist on the active Run axis or its channel values are unusable
- **THEN** the topomap reports a structured unavailable or invalid-field state
- **AND** it does not select a nearby bin or a previous window

#### Scenario: Playback advances while a PSD topomap is loading
- **WHEN** Review advances to a newer eligible PSD window before the previous topomap response completes
- **THEN** the older response cannot replace the newer window's field
- **AND** the waveform remains controlled by the single Review time coordinator
