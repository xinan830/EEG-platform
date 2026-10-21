# analysis/official-algorithm-foundation Specification

## Purpose
TBD - created by archiving change add-official-algorithm-parameter-contract. Update Purpose after archive.
## Requirements
### Requirement: Execute only registered official modules

Every new executable algorithm SHALL be a registered backend module with a
stable algorithm ID, scientific version, implementation identity, input and
output contracts, parameter schema, quality contract, evidence snapshot, and
independent tests. The generic Run service SHALL dispatch through the registry
and SHALL NOT branch on concrete algorithm IDs.

#### Scenario: Register an official module

- **WHEN** an official module is installed
- **THEN** the catalog exposes its identity, supported modes, parameters,
  output unit, quality behavior, and implementation identity

#### Scenario: Unknown module

- **WHEN** a client submits an unregistered algorithm ID or unavailable version
- **THEN** the backend rejects the request before reading EEG data or queuing a
  completed Run

### Requirement: Keep reusable scientific primitives separate

The backend SHALL expose reusable, versioned scientific primitive contracts for
Welch PSD, band power, peak frequency, and STFT time-frequency analysis.
Sample-aligned sliding windows SHALL be execution primitives shared by static
and dynamic Runs. Primitive outputs SHALL declare units, frequency/time axes,
channel order, canonical sample range, derived time range, and
quality/provenance.

#### Scenario: Compute a PSD

- **WHEN** a valid V/float64 EEG window is sent to the PSD primitive
- **THEN** the scientific result declares `V^2/Hz`, sampling rate, frequency bins,
  channel order, actual range, Welch parameters, and quality evidence

#### Scenario: Preserve channel order

- **WHEN** channels are requested as `Oz,Fz,Pz`
- **THEN** every primitive output and evidence record preserves that exact order

### Requirement: Separate mathematical legality from preset semantics

Generic primitives SHALL enforce only mathematical and unit legality. Official
presets SHALL enforce semantic rules such as standard Theta/Beta bands or the
Alpha meaning of IAPF. A general band ratio MAY accept overlapping bands when
mathematically valid, while the standard Theta/Beta preset MAY reject overlap.

#### Scenario: Parameterize peak frequency

- **WHEN** the generic peak-frequency primitive is configured for a Beta band
- **THEN** the output is identified as peak frequency in Beta and is not
  mislabeled as IAPF

#### Scenario: Standard Theta/Beta preset

- **WHEN** a standard Theta/Beta preset is configured
- **THEN** its numerator and denominator bands are validated against the
  preset's semantic contract and the actual bands are saved in evidence

### Requirement: Use SI units and sample coordinates as canonical science data

Scientific primitives and algorithm modules SHALL use `V`, `V^2/Hz`, `V^2`,
`Hz`, and `s` internally, with no implicit scale conversion. Every input and
output range SHALL use recording-relative sample indices with inclusive start
and exclusive end; seconds SHALL be derived using the validated sampling rate.

#### Scenario: Resolve a UI time range

- **WHEN** a client submits a range in seconds
- **THEN** the backend resolves it once to `[start_sample, end_sample)` before
  cache identity, event alignment, windowing, or algorithm execution

#### Scenario: Convert display units

- **WHEN** an API or export chooses microvolt units
- **THEN** it applies an explicit recorded conversion from the SI result and
  retains the internal SI value as the scientific source

### Requirement: Apply layered preprocessing and quality contracts

Each Run SHALL record the resolved channel/montage, preprocessing, gap policy,
and quality policy. Quality SHALL be represented in three layers:
`DataIntegrity`, `SignalQuality`, and `AlgorithmValidity`. Each layer SHALL
provide a stable status, reason list, and metrics. Shared quality checks SHALL
identify non-finite samples, missing samples, amplitude anomalies, clipping,
and flatline where applicable; algorithm modules MAY add stricter gates.
Rejected scientific values SHALL be unavailable with a stable reason and SHALL
never be replaced by zero.

#### Scenario: Window contains a gap

- **WHEN** an analysis window contains missing samples or a recording gap
- **THEN** the window reports its gap/quality state and the algorithm either
  applies its declared policy or returns an unavailable result; it does not
  silently fill the gap with fabricated EEG

#### Scenario: Distinguish transform padding

- **WHEN** an FFT or STFT primitive uses zero-padding to satisfy its declared
  transform length
- **THEN** the evidence labels it as transform padding and does not report it
  as recovered EEG or as gap imputation

### Requirement: Use one execution model for static, dynamic, and box selection

Static execution SHALL consume one canonical sample range. Dynamic execution
SHALL consume one canonical analysis range, sample-aligned window length, and
sample-aligned step. UI box selection SHALL produce a static sample-range
request and SHALL use the same Runtime and provenance path.

#### Scenario: Dynamic partial window

- **WHEN** playback reaches less than the configured trailing window but at
  least the primitive's minimum valid duration
- **THEN** the backend returns an explicitly marked `Partial` point using the
  actual available range

#### Scenario: Dynamic point evidence

- **WHEN** a dynamic point is returned
- **THEN** it includes window start/end, anchor semantics, valid/rejected sample
  counts, gap state, quality, and the point's parameter/primitive evidence

### Requirement: Declare multi-field output schemas

Every official module SHALL declare an `output_schema` containing one typed,
unit-bearing declaration per output field, including shape and meaning where
applicable. A single module-level output unit SHALL not be used to describe
multi-value or multi-axis results.

#### Scenario: Inspect a time-frequency result

- **WHEN** a client reads an STFT catalog entry
- **THEN** it can distinguish power values, frequency axis, time axis, and
  their respective units without inferring them from an algorithm name

### Requirement: Keep display state out of scientific execution

Timebase, paper speed, sensitivity, color, zoom, chart history, and playback
panel state SHALL remain display-local. Changing them SHALL not create a new
scientific Run or change an existing numerical result.

#### Scenario: Change paper speed

- **WHEN** a user changes paper speed while viewing an analysis
- **THEN** the waveform presentation changes without changing Run identity,
  cache identity, parameters, or result values

### Requirement: Preserve historical user-defined results during governance migration

The platform MAY disable creation or execution of user-defined algorithms, but
it SHALL keep historical user-defined Runs, results, evidence, and artifacts
readable. Disabling new execution SHALL not delete or reinterpret history.

#### Scenario: Inspect a retired user algorithm Run

- **WHEN** a historical Run references a user-defined algorithm that is no
  longer executable
- **THEN** the backend returns immutable stored provenance and marks execution
  unavailable without importing or executing the retired definition

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
