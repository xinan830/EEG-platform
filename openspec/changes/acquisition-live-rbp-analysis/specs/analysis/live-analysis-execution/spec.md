# Spec Delta

## Purpose

Defines the backend contract for scientific algorithms that consume live acquisition samples, including RBP, without treating acquisition data as a persisted offline Analysis Run.

## ADDED Requirements

### Requirement: Validate live algorithm signal input
The live-analysis service SHALL accept sample-major float64 EEG values in V together with the actual sampling rate in Hz, ordered channel metadata, first sample counter, and acquisition session identity. It SHALL select only explicitly identified EEG channels in the requested order and reject missing, duplicated, mismatched, or unsupported inputs.

#### Scenario: Batch contains auxiliary channels
- **WHEN** a live batch includes EEG, sample-counter, or trigger columns
- **THEN** RBP receives only the explicitly selected EEG columns in the declared order
- **AND** auxiliary columns are not interpreted as EEG

#### Scenario: Sampling rate cannot support RBP
- **WHEN** the actual sampling rate has Nyquist frequency at or below 50 Hz
- **THEN** the service returns a structured unsupported-input result
- **AND** it does not publish RBP values

### Requirement: Preserve live sample continuity and coordinates
The live-analysis service SHALL associate every input and output with its acquisition session and sample-counter range. It SHALL not interpolate, zero-fill, or join data across a missing, duplicated, reset, or out-of-order sample counter.

#### Scenario: Sample counter gap occurs
- **WHEN** the next batch begins after a missing sample-counter interval
- **THEN** the service marks the interval as a gap, invalidates windows spanning it, and resumes only from sufficient contiguous real samples
- **AND** emitted result coordinates identify the actual sample and elapsed-time bounds

#### Scenario: Session identity changes
- **WHEN** samples arrive for a new acquisition session
- **THEN** prior session buffers and preprocessing state are not reused

### Requirement: Apply and identify RBP live preprocessing
Live RBP SHALL use the current five-band formulas, declared channel-specific quality rules, and an explicit continuous preprocessing contract. Each result SHALL identify the actual preprocessing scope, filter and notch settings, sample/time window, channel order, algorithm implementation identity, quality state, and unavailable-channel reasons. It SHALL not claim equivalence with offline `official-rbp-v4` unless numerical equivalence is validated for the live execution path.

#### Scenario: Live RBP result is published
- **WHEN** a contiguous live window is processed successfully or partially
- **THEN** the result reports Delta, Theta, Alpha, Beta, and Gamma relative power with explicit units and channel order
- **AND** artifact warnings and per-channel unavailable values retain their quality state and reason without imputation

#### Scenario: Live preprocessing differs from offline RBP
- **WHEN** streaming state or window-local processing does not match the offline continuous-before-slice preprocessing contract
- **THEN** the result declares a distinct live implementation/scientific identity and preprocessing evidence
- **AND** the offline algorithm version and historical result identity remain unchanged

### Requirement: Bound live analysis work and report overload
The live-analysis service SHALL process samples through bounded buffering and SHALL expose queue overrun, worker failure, cancellation, and warm-up as structured live-analysis states. It SHALL not persist live output as an Analysis Run or alter saved recording data.

#### Scenario: Live analysis cannot keep up
- **WHEN** incoming samples exceed the configured bounded live-analysis capacity
- **THEN** the service reports overload and invalidates any affected scientific windows
- **AND** it resumes only when it can establish a valid contiguous input range

#### Scenario: Live result is consumed
- **WHEN** the service publishes a live result
- **THEN** the result remains ephemeral to the acquisition session and is not written to Run, result, or backtrace persistence
