## ADDED Requirements

### Requirement: Declare dynamic strategy capabilities per algorithm

Every official algorithm SHALL declare support status for full dynamic
analysis, on-demand dynamic analysis, and combined on-demand preview plus
background full analysis. It SHALL declare full-result-to-preview projection
and on-demand-result-to-full reuse independently, including exact identity and
window conditions or an explicit unsupported/unvalidated status. The platform
SHALL manage these declarations and strategy gating consistently without
assuming identical support across algorithms. This management metadata SHALL
NOT contain or replace the algorithm's scientific formula.

#### Scenario: Inspect dynamic capabilities

- **WHEN** a client or release completeness check reads an official algorithm declaration
- **THEN** it can distinguish full, on-demand, and combined strategy support
- **AND** it can separately determine full-result-to-preview and on-demand-result-to-full reuse status and identity contract

#### Scenario: Declare a strategy unsupported

- **WHEN** an algorithm has no validated implementation or safe reuse contract for one strategy or reuse direction
- **THEN** its declaration marks that strategy unsupported with a structured reason
- **AND** the system does not infer support solely because the algorithm supports dynamic windows or the opposite reuse direction

#### Scenario: Keep calculation independent

- **WHEN** the shared runtime orchestrates any declared strategy
- **THEN** it delegates each scientific window calculation to that algorithm's module
- **AND** shared orchestration does not implement or duplicate the algorithm's formula

### Requirement: Centrally orchestrate the three dynamic strategies

The existing shared runtime SHALL provide common lifecycle, scheduling,
cancellation, status, window identity, persistence, and safe reuse behavior for
full dynamic, on-demand dynamic, and combined preview-plus-background-full
execution. This requirement does not require rebuilding existing orchestration.
Algorithm modules SHALL own their scientific calculations and SHALL declare
strategy capability and directional reuse eligibility through the management
contract.

#### Scenario: Combined strategy shares an exact window

- **WHEN** preview and background full analysis request the same source, current algorithm identity, scientific config, preprocessing, and canonical sample range
- **THEN** the shared runtime executes or joins that window once and makes the validated result available to both consumers
- **AND** it records the producing/serving strategy in provenance

#### Scenario: Combined strategy has incompatible windows

- **WHEN** preview and background analysis differ in source, identity, config, preprocessing, or canonical sample range
- **THEN** the shared runtime does not reuse the preview result for full analysis
- **AND** each requested window follows its declared calculation path

#### Scenario: Reuse directions differ by algorithm

- **WHEN** an algorithm declares full-result-to-preview reuse but not on-demand-result-to-full reuse, or vice versa
- **THEN** the shared runtime enables only the declared direction
- **AND** it does not infer that the directions are interchangeable

#### Scenario: Cancel one strategy consumer

- **WHEN** a user cancels preview or background full work while another consumer still depends on a shared in-flight window
- **THEN** the shared lifecycle cancels only the requesting subscription unless no consumers remain
- **AND** unrelated strategy work and persisted completed results remain governed by the common cancellation contract

### Requirement: Validate official algorithm management completeness

The platform SHALL provide a deterministic completeness check for every
official algorithm module across the backend catalog and supported client
integration points. The check SHALL identify missing identity, parameters,
capabilities, output declarations, result handling, or required validation
coverage before an algorithm is considered ready for release. For each
strategy and reuse direction, the check SHALL distinguish implementation
status from validation status; a declared or implemented capability without
required evidence SHALL remain unvalidated and SHALL NOT be reported as
release-ready. Missing items SHALL identify the algorithm ID, integration
layer, and exact declaration, registration, test, or evidence item required.

#### Scenario: Validate a complete official algorithm

- **WHEN** all required backend and desktop registrations and contracts exist for an official module
- **THEN** the completeness check reports the module as ready and lists its current scientific and implementation identities
- **AND** it links the required declarations, registrations, strategy tests, and validation evidence for that module

#### Scenario: Detect a missing client integration

- **WHEN** an official module is absent from a required desktop detail or result-handler registry
- **THEN** the completeness check fails and names the missing integration point
- **AND** the check does not require or inspect the module's scientific formula

#### Scenario: Capability is implemented but not validated

- **WHEN** an algorithm declares or implements a strategy but required strategy-specific validation evidence is absent
- **THEN** the completeness check reports the capability as implemented but unvalidated
- **AND** it does not report the algorithm as release-ready for that capability

#### Scenario: Independent modes do not prove combined mode

- **WHEN** full and on-demand strategies pass independently but combined preview-plus-background-full evidence is absent
- **THEN** combined mode remains unvalidated
- **AND** the release check identifies the missing combined-path test or evidence

#### Scenario: Onboard a new official algorithm

- **WHEN** a new official algorithm is added
- **THEN** the onboarding checklist identifies its authoritative module declaration, backend registration, required WPF registrations, tests, and validation artifacts
- **AND** every required item is linked to an automated completeness check or an explicit documented evidence requirement
- **AND** a missing item is reported by algorithm ID and integration layer

#### Scenario: Trace a current algorithm result

- **WHEN** a current algorithm produces a Run or dynamic result
- **THEN** its traceable record identifies the scientific and implementation identities, frozen configuration, selected strategy, actual sample bounds, and producing/serving strategy provenance
- **AND** the validation record identifies the tested algorithm identity and reproducible test or evidence artifact

#### Scenario: Retire a scientific version

- **WHEN** a scientific version becomes retired
- **THEN** stored identity metadata remains distinguishable for audit where present
- **AND** the current application is not required to execute, migrate, interpret, or display that version's stored result

#### Scenario: Add an algorithm-owned scientific implementation

- **WHEN** a new official algorithm is introduced
- **THEN** its scientific calculation, specialized channel relationships, and algorithm-specific evidence are implemented in that algorithm's own module
- **AND** shared runtime services only validate, schedule, track provenance, persist, and transport its declared result

#### Scenario: Advance the current version

- **WHEN** an official algorithm advances to a new scientific version
- **THEN** the new version becomes the single explicitly current selectable/executable version
- **AND** the platform does not require a compatibility implementation for the retired calculator
