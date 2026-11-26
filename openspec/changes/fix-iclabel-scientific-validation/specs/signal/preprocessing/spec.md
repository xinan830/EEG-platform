## ADDED Requirements

### Requirement: Preserve the ICA fit reference during classification
ICLabel SHALL classify unchanged ICA fit samples only when the complete unique fitted EEG population matches the common-average reference pool and samples satisfy that reference numerically. Other references SHALL return an explicit fallback reason without re-referencing. Units, channel order and recording time SHALL remain unchanged.

#### Scenario: Partial average reference
- **WHEN** an average-reference pool contains only a subset of fitted EEG sites
- **THEN** ICA remains reviewable but ICLabel is unavailable for that reference and fit samples are not modified

### Requirement: Base automatic recommendations on verified stopping evidence
ICA convergence SHALL use full-precision evidence for the declared solver stopping criteria. A rounded change or unverified stop SHALL NOT enable automatic recommendations.

#### Scenario: Iteration limit with rounded zero
- **WHEN** the last reported change rounds to zero but exceeds the true tolerance and no other stopping rule is met
- **THEN** ICA is reported as unverified or not converged and automatic recommendations are disabled

### Requirement: Permit ICA review without correlation references
ICA fitting and eligible ICLabel classification SHALL remain available without EOG, ECG or permission to use frontal EEG proxies. Correlation scores SHALL remain absent when their references are unavailable, and explicit invalid references SHALL fail validation.

#### Scenario: CAR with no auxiliary references
- **WHEN** valid CAR data is reviewed with frontal proxy use disabled and no auxiliary selection
- **THEN** classification completes without selecting frontal EEG as an implicit reference

### Requirement: Report classifier runtime unavailability explicitly
Missing classifier assets and known inference-runtime failures SHALL return a structured unavailable reason and retain any valid correlation scores. New scientific behavior SHALL invalidate stale review/application cache identities.

#### Scenario: ONNX weights are missing or invalid
- **WHEN** model loading or ONNX execution fails for a known runtime cause
- **THEN** review returns explicit unavailable state without generic HTTP 500 or fabricated classifications

#### Scenario: Model asset is restored or replaced without restarting
- **WHEN** a previously missing model asset is restored, deleted or replaced between review requests
- **THEN** subsequent requests report the current availability and model identity rather than reusing stale hash evidence

### Requirement: Synchronize scientific notch settings with broadband ICA fitting
Broadband ICA fitting SHALL use the selected scientific notch frequency and Q, including disabled notch. Review and execution SHALL share preparation and cache identity, expose and persist `fit_notch_hz`, and invalidate old identities. Recording data and independent display filtering SHALL remain unchanged.

#### Scenario: Selected 50 Hz notch during ICA review
- **WHEN** ICA review uses a 50 Hz scientific notch
- **THEN** broadband fit preparation applies that notch and its Q, reports 50 Hz in fit evidence, and retains the same evidence after persisted model restoration

#### Scenario: Scientific notch disabled
- **WHEN** ICA review is prepared with no scientific notch
- **THEN** broadband fit preparation omits notch filtering, reports a null fit notch, and uses a different identity from the notched request
