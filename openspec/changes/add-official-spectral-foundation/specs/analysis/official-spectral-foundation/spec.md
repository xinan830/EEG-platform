# Official spectral foundation

## ADDED Requirements

### Requirement: Provide an explicit SI-unit real FFT primitive

The backend SHALL provide a pure real FFT primitive that accepts one or more
channels of finite `float64` samples in volts and returns a non-negative
frequency axis in Hz plus complex Fourier coefficients in the requested
channel order. The primitive SHALL use recording-relative half-open sample
coordinates and SHALL not apply filtering, rereferencing, or unit conversion.

#### Scenario: Transform a two-channel window

- **WHEN** a finite `(samples, channels)` V-valued window and positive sampling
  rate are provided
- **THEN** the result contains `rfft` frequencies in Hz, coefficients ordered
  by channel, the original sample range, and the requested sampling rate

### Requirement: Distinguish input gaps from transform padding

The primitive SHALL reject non-finite input with a gap reason and SHALL never
replace missing samples with zeros. If a transform length larger than the
input length is explicitly requested, the result SHALL report the added
samples as `TransformPaddingEvidence(kind="fft_boundary")`; those samples
SHALL not be reported as recovered EEG or as a recording gap.

#### Scenario: Explicit FFT boundary padding

- **WHEN** `transform_length` is larger than the input sample count
- **THEN** the transform uses zero-padding only for the mathematical FFT and
  reports the exact padding count separately from acquisition quality

### Requirement: Reject invalid transform requests

The primitive SHALL reject empty input, non-positive sampling rates, invalid
dimensions, non-finite values, transform lengths shorter than the input, and
non-integral transform lengths. It SHALL not silently infer or resample a
sampling rate.

#### Scenario: Invalid transform length

- **WHEN** a transform length is shorter than the input or is not an integer
- **THEN** the backend raises a validation error before numerical execution
