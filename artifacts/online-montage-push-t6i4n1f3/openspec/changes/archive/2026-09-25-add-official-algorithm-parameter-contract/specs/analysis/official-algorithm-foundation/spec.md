# analysis/official-algorithm-foundation Specification

## ADDED Requirements

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
