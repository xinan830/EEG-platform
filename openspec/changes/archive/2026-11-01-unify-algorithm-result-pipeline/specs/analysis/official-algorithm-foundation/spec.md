## MODIFIED Requirements

### Requirement: Declare multi-field output schemas

Every official module SHALL declare an `output_schema` containing one typed,
unit-bearing declaration per output field, including shape and meaning where
applicable. A single module-level output unit SHALL not be used to describe
multi-value or multi-axis results. An official module that exposes completed
full-run windows SHALL also declare the shared result descriptor: scientific
array names, axis names and units, channel-order semantics, absolute versus
relative time semantics, required provenance fields, and compatibility versions.
The generic full-run reader SHALL consume this descriptor instead of branching
on concrete algorithm IDs.

#### Scenario: Inspect a time-frequency result

- **WHEN** a client reads an STFT catalog entry
- **THEN** it can distinguish power values, frequency axis, time axis, and
  their respective units without inferring them from an algorithm name

#### Scenario: Inspect a frequency-series result

- **WHEN** a client reads a PSD catalog entry
- **THEN** it can identify the spectral array, frequency axis, channel order,
  and declared power unit from the output schema and descriptor

#### Scenario: Register a full-run descriptor

- **WHEN** an official module exposes full-run windows
- **THEN** registration fails unless its descriptor declares every array and
  axis needed by the shared reader, including time and provenance semantics
