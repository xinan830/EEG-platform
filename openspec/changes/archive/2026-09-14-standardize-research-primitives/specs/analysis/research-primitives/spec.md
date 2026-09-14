## Purpose

Define safe, typed and unit-aware backend building blocks that can express reusable EEG research calculations while preserving channel order, time, quality, and provenance.

## ADDED Requirements

### Requirement: Represent scientific values explicitly

The primitive layer SHALL represent `EEGSignal`, `WindowedSignal`, `PSDSeries`, `BandPower`, `RelativePower`, `Scalar`, `TimeSeries`, `ChannelMap`, and `QualityMask` with explicit units, ordered channels, time metadata, quality, and provenance appropriate to each type.

#### Scenario: Select channels
- **WHEN** channels are selected as `Oz,Fz,Pz` from an EEG signal
- **THEN** data and metadata preserve that exact order and append the selection to provenance

### Requirement: Enforce units without implicit conversion

The primitive layer SHALL distinguish V, uV, V^2, uV^2, V^2/Hz, uV^2/Hz, Hz, s, ratio, percent, dimensionless, and referenced dB, and SHALL reject incompatible arithmetic unless conversion is explicit.

#### Scenario: Add incompatible values
- **WHEN** a node attempts to add `uV^2` and `Hz`
- **THEN** evaluation fails with a typed unit error before producing output

### Requirement: Provide safe reusable nodes

The registry SHALL expose channel selection, rereference, bandpass, notch, resample, detrend, window, Welch PSD, band power, RBP, add, subtract, multiply, divide, natural logarithm, mean, median, standard deviation, CV, weighted sum, quality gate, and output nodes through declared input/output contracts without arbitrary evaluation.

#### Scenario: Resolve an unknown node
- **WHEN** a caller requests an unregistered node type
- **THEN** the registry rejects it and executes no user-provided code

### Requirement: Define sampling and window semantics

Resampling SHALL use an explicit anti-alias method. Windowing SHALL declare length, step, alignment, time centers, and residual-window policy in seconds and sample indices.

#### Scenario: Window ten seconds
- **WHEN** a 10-second signal is windowed into 4-second windows with a 2-second step and dropped residuals
- **THEN** four windows have centers at 2, 4, 6, and 8 seconds relative to the signal start

### Requirement: Propagate quality and provenance

Every node SHALL preserve or combine input quality and append its node type plus resolved parameters to provenance. A quality gate SHALL produce an unavailable typed value with reasons rather than a zero-valued measurement.

#### Scenario: Gate a bad PSD
- **WHEN** a PSD carries a rejected quality mask
- **THEN** downstream output remains typed but unavailable, preserves reasons, and does not contain fabricated zero power

### Requirement: Express reference algorithms

The primitives SHALL be sufficient to construct fixed-band RBP and FAA entirely in the backend with explicit input channels, units, logarithm semantics, and ordered provenance.

#### Scenario: Construct FAA
- **WHEN** alpha band power for F3 and F4 is positive and expressed in the same power unit
- **THEN** `ln(F4 alpha power) - ln(F3 alpha power)` returns a dimensionless scalar with traceable inputs
