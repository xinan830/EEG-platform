## Purpose

Defines distinct, traceable classic and individualized BrainBeat metrics so that fixed-band absolute-power results cannot be confused with the existing IAPF-relative relative-power calculation.

## ADDED Requirements

### Requirement: Calculate classic BrainBeat as a fixed-band absolute-power ratio
The classic `brainbeat` algorithm SHALL use the explicitly mapped Fz and Pz inputs in that order. It SHALL calculate the absolute Fz power in the fixed 4–8 Hz Theta band and divide it by the absolute Pz power in the fixed 8–12 Hz Alpha band. It SHALL NOT normalize either power by broadband power or depend on IAPF. The output SHALL be a dimensionless ratio and SHALL include the effective bands, source roles, intermediate band powers with units, and formula provenance.

#### Scenario: Compute classic BrainBeat from distinct source channels
- **WHEN** valid Fz/Pz spectra contain integrable 4–8 Hz and 8–12 Hz bands
- **THEN** the result equals `absolute_power(Fz, 4–8 Hz) / absolute_power(Pz, 8–12 Hz)` within numerical tolerance
- **AND** changing power outside those bands alone does not change either intermediate band power

#### Scenario: Classic BrainBeat does not require IAPF
- **WHEN** the shared spectrum passes quality checks but has no valid IAPF estimate
- **THEN** classic BrainBeat remains computable from its fixed bands
- **AND** its provenance does not claim an IAPF dependency

#### Scenario: Reject an unavailable classic band
- **WHEN** either required band cannot be integrated on the actual frequency axis or either channel is unavailable
- **THEN** the result is null with a structured reason
- **AND** no epsilon-derived fabricated power is reported as a valid intermediate value

### Requirement: Preserve the individualized BrainBeat as a separate scientific identity
The existing IAPF-relative formula SHALL be exposed as `brainbeat_individualized` and SHALL remain explicitly distinct from classic BrainBeat in catalog metadata, Run configuration, result provenance, and review presentation. It SHALL retain its IAPF-derived Theta and Alpha bands and per-channel 1–30 Hz relative-power normalization. Historical Runs produced under the former `brainbeat` identity SHALL remain immutable and readable as stored results; the platform SHALL NOT reinterpret or rewrite them as classic BrainBeat.

#### Scenario: Select the individualized metric
- **WHEN** a caller requests `brainbeat_individualized`
- **THEN** the result uses its declared IAPF-relative relative-power formula
- **AND** the result identity and display label distinguish it from classic BrainBeat

#### Scenario: Read a historical BrainBeat Run
- **WHEN** a previously stored BrainBeat Run is retrieved after the identity change
- **THEN** its stored scientific version, result bytes, formula evidence, and value remain unchanged
- **AND** no retired calculator is invoked to regenerate or convert the result

### Requirement: Use one explicit dynamic window policy for both BrainBeat variants
Classic and individualized BrainBeat SHALL declare a 20-second dynamic analysis window and a one-second refresh step by default, with supported complete windows of 10, 20, and 30 seconds and a 10-second minimum. They SHALL NOT emit numeric warm-up results before a complete selected window is available. Each window SHALL use the shared four-second Hann Welch estimator with 50% overlap and shared spectral quality gate; this outer window policy SHALL NOT alter Welch parameters.

#### Scenario: First complete BrainBeat window
- **WHEN** a dynamic BrainBeat run starts at zero and reaches 20 seconds
- **THEN** it calculates the 0–20 second window
- **AND** it emits no numeric result for earlier partial data

#### Scenario: Advance BrainBeat windows once per second
- **WHEN** the cursor reaches 21 and 22 seconds with a 20-second window
- **THEN** the actual windows are 1–21 and 2–22 seconds
- **AND** every point records its actual absolute sample-time range

#### Scenario: Resolve identical settings across execution paths
- **WHEN** Official Run or on-demand review omits the BrainBeat window settings
- **THEN** both resolve to 20 seconds and one second from the selected variant's catalog policy
- **AND** explicit supported window selections are preserved

### Requirement: Retain shared spectral quality and preprocessing evidence
Both BrainBeat variants SHALL use the shared analysis preprocessing and Welch spectral source, including explicit Fz/Pz role order, the configured optional 50/60 Hz notch, the 1–50 Hz analysis bandpass, and the shared 75% minimum clean-segment ratio. A failed quality gate SHALL produce a null result with quantitative quality evidence and rejected-segment reasons. Missing samples and non-finite samples SHALL be rejected rather than imputed.

#### Scenario: Reject a low-quality paired window
- **WHEN** fewer than 75% of the window's Welch segments pass the shared quality rules
- **THEN** the BrainBeat value is null with a quality-gate failure and clean/total segment counts
- **AND** neither variant falls back to a previous window's value

#### Scenario: Record preprocessing and spectral identity
- **WHEN** a BrainBeat result is persisted
- **THEN** provenance identifies source channel order, sample rate, notch choice, filter contract, actual window, Welch segment and overlap, quality state, and scientific version
