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

#### Scenario: A reference was previously selected as a bad EEG channel
- **WHEN** a dedicated/non-EEG reference or its verified canonical alias also occurs in the bad-channel specification
- **THEN** source partition removes only that overlap from the effective EEG bad-channel policy before interpolation or montage projection, while unrelated invalid bad-channel names still fail validation

#### Scenario: Different native voltage clocks
- **WHEN** EDF/BDF voltage sources have different native sample rates
- **THEN** reference discovery/preparation fails explicitly with an alignment error and performs no implicit resampling

#### Scenario: Algorithms consume the effective EEG layout
- **WHEN** dedicated auxiliary sources also occur in raw source columns or montage inputs
- **THEN** the shared analysis entry obtains channel availability from the execution reader and all algorithm panels request that EEG layout without reintroducing auxiliary sources or unavailable derived outputs

### Requirement: Artifact reference selection invalidates reviewed identity
Changing reference selection SHALL invalidate pending and confirmed ICA choices and scientific comparisons. Cache and evidence identities SHALL bind source, selection and reference-processing semantics. Empty selections SHALL preserve documented auto/proxy behavior.

#### Scenario: Selection changes during review
- **WHEN** the user changes auxiliary selection while an ICA request is pending
- **THEN** the old response cannot confirm or overwrite the new selection

#### Scenario: Bad-segment inspection follows effective reference roles
- **WHEN** the user scans before ICA confirmation, switches removal method, confirms ICA, or invalidates a confirmed model
- **THEN** the scan uses the effective preview payload and reports from a different reference population cannot remain visible

#### Scenario: Reference roles conflict
- **WHEN** the same source is selected for both EOG and ECG
- **THEN** stale scientific work is canceled, new preview, bad-segment, ICA and algorithm requests are blocked, and normal preparation resumes after reselection

#### Scenario: Reference output is absent in a scientific comparison
- **WHEN** the backend lists an output as excluded because of reference partitioning
- **THEN** the desktop displays its excluded badge regardless of the bad-channel policy

#### Scenario: Automatic ECG selection is displayed
- **WHEN** no explicit ECG source is selected
- **THEN** the rounded selector displays an explicit automatic-selection label while retaining the existing empty selection in the request contract

#### Scenario: Multiple dedicated ECG sources are available
- **WHEN** ICA is requested without an explicit ECG selection and multiple sources have dedicated ECG identity
- **THEN** review and execution require an explicit single selection with actionable guidance instead of choosing by file order

#### Scenario: An inactive ECG choice has incompatible identity
- **WHEN** an invalid ECG identity was selected and the user switches away from ICA
- **THEN** that inactive ECG identity check no longer blocks other methods, while explicit ICA fitting still validates it and unsaved identities or cross-role overlap remain blocked

#### Scenario: Identity editing pauses scientific comparison
- **WHEN** identity changes have not been saved
- **THEN** comparison immediately displays the same unsaved-identity reason as payload validation, rather than reporting an EOG/ECG conflict

### Requirement: Physiological identity is separate from scoring use
Each recorded voltage source SHALL have a source-bound identity: EEG, dedicated EOG, dedicated ECG or unconfirmed. Review SHALL allow saving that declaration for the current recording. Scoring selection SHALL NOT change identity. Dedicated and unconfirmed sources SHALL stay outside scientific EEG even when removal is disabled. Recorded EEG membership SHALL NOT be inferred solely from channel names.

#### Scenario: The same EEG proxies are chosen automatically or manually
- **WHEN** the same recording, montage, filters and ICA settings select the same EEG proxies automatically or explicitly
- **THEN** EEG fitting channels, component scores, recommendations and reconstructed samples are numerically identical and those proxies remain EEG outputs

#### Scenario: Dedicated physiological electrodes are declared
- **WHEN** a raw voltage source is saved as dedicated EOG or ECG
- **THEN** EEG fitting, interpolation donors, CAR and derived outputs exclude that source while separate scoring retains its aligned samples; ECG scoring requires dedicated ECG identity

#### Scenario: Identity edits have not been saved
- **WHEN** a user changes identity without a successful save or selects incompatible scoring roles
- **THEN** stale work is canceled and new preview, scan, ICA and algorithm requests are blocked until inputs are valid and saved

#### Scenario: Identity changes while work is queued or running
- **WHEN** saved physiological identities differ from the scientific input snapshot
- **THEN** old sessions and queued/running work cannot publish under the obsolete identity

### Requirement: Recording identities survive reopen and signal roundtrip
Saved identities SHALL be source-bound metadata separate from acquired samples. EDF/BDF export SHALL retain voltage auxiliary sources and per-column identities in accompanying metadata. Import SHALL validate source hash, columns, roles and voltage units before publication.

#### Scenario: EEG and auxiliary sources are exported and imported
- **WHEN** exported EDF/BDF and matching labels metadata are imported
- **THEN** identities, raw column order and common sample clock are restored, including an auxiliary voltage source confirmed as EEG

#### Scenario: Scientific comparison contains a display-only auxiliary row
- **WHEN** an ordinary display row is absent from both scientific sides because of validated identity partition
- **THEN** the backend declares that row excluded and other EEG comparisons stay available with an excluded badge and no fabricated auxiliary trace
