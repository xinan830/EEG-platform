## ADDED Requirements

### Requirement: User-selected artifact reference channels
The system SHALL let Review users select multiple recorded voltage channels as EOG references and at most one as ECG reference. Explicit choices SHALL use raw source labels, reject role overlap and unavailable or non-voltage channels, and be shown in ICA review evidence.

#### Scenario: Explicit auxiliary choices
- **WHEN** the user selects recorded eye and heart channels and requests ICA review
- **THEN** the request and confirmed execution use those channels rather than silently selecting frontal proxies

#### Scenario: Invalid reference
- **WHEN** a selected channel is missing, non-voltage or assigned both roles
- **THEN** preparation fails explicitly without producing a cleaned result

### Requirement: Separate aligned artifact reference signals
Auxiliary references SHALL remain V/float64 on the recording absolute sample clock and SHALL NOT become EEG ICA fitting channels, EEG montage reference inputs, interpolation donors, topomap channels or spectral outputs. Selected references SHALL reach review and execution identically.

#### Scenario: Montage hides an auxiliary source
- **WHEN** an EOG or ECG source is absent from the selected EEG montage
- **THEN** scoring can still read its verified raw samples and EEG output order remains unchanged

#### Scenario: Reference gaps
- **WHEN** a selected auxiliary reference is unavailable during a sample interval
- **THEN** those values are not replaced with zeros or treated as valid scoring samples

#### Scenario: Different native voltage clocks
- **WHEN** EDF/BDF voltage sources have different native sample rates
- **THEN** reference discovery/preparation fails explicitly with an alignment error and performs no implicit resampling

#### Scenario: Algorithms consume the effective EEG layout
- **WHEN** selected auxiliary sources also occur in raw EEG or montage inputs
- **THEN** the shared analysis entry obtains channel availability from the execution reader and all algorithm panels request that EEG layout without reintroducing auxiliary sources or unavailable derived outputs

### Requirement: Artifact reference selection invalidates reviewed identity
Changing reference selection SHALL invalidate pending and confirmed ICA choices and scientific comparisons. Cache and evidence identities SHALL bind source, selection and reference-processing semantics. Empty selections SHALL preserve documented auto/proxy behavior.

#### Scenario: Selection changes during review
- **WHEN** the user changes auxiliary selection while an ICA request is pending
- **THEN** the old response cannot confirm or overwrite the new selection
