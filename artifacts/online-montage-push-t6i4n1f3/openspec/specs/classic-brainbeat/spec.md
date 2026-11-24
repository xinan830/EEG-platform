## Purpose

Defines distinct, traceable classic and individualized BrainBeat metrics so
fixed-band absolute-power results cannot be confused with the IAPF-relative
relative-power calculation.

## Requirements

### Requirement: Calculate classic BrainBeat as a fixed-band absolute-power ratio

Classic `brainbeat` SHALL use explicitly mapped Fz and Pz inputs in that
order. It SHALL calculate absolute Fz power in the fixed 4–8 Hz Theta band
and divide it by absolute Pz power in the fixed 8–12 Hz Alpha band. It SHALL
NOT normalize either power by broadband power or depend on IAPF. The output
SHALL be a dimensionless ratio and SHALL include effective bands, source
roles, intermediate band powers with units, and formula provenance.

#### Scenario: Compute classic BrainBeat from distinct source channels
- **WHEN** valid Fz/Pz spectra contain integrable 4–8 Hz and 8–12 Hz bands
- **THEN** the result equals absolute Fz Theta power divided by absolute Pz Alpha power within numerical tolerance
- **AND** changing power outside those bands alone does not change either intermediate band power

#### Scenario: Classic BrainBeat does not require IAPF
- **WHEN** the shared spectrum passes quality checks but has no valid IAPF estimate
- **THEN** classic BrainBeat remains computable from its fixed bands
- **AND** its provenance does not claim an IAPF dependency

#### Scenario: Reject an unavailable classic band
- **WHEN** either required band cannot be integrated on the actual frequency axis or either channel is unavailable
- **THEN** the result is null with a structured reason
- **AND** no epsilon-derived fabricated power is reported as valid

### Requirement: Preserve individualized BrainBeat as a separate scientific identity

The existing IAPF-relative formula SHALL be exposed as
`brainbeat_individualized` and remain distinct from classic BrainBeat in
catalog metadata, Run configuration, result provenance, and review
presentation. It SHALL retain its IAPF-derived bands and per-channel 1–30 Hz
relative-power normalization. Historical Runs under the former `brainbeat`
identity SHALL remain immutable and readable as stored results; the platform
SHALL NOT reinterpret or rewrite them as classic BrainBeat.

#### Scenario: Select the individualized metric
- **WHEN** a caller requests `brainbeat_individualized`
- **THEN** the result uses its declared IAPF-relative formula
- **AND** its result identity and display label distinguish it from classic BrainBeat

#### Scenario: Read a historical BrainBeat Run
- **WHEN** a previously stored BrainBeat Run is retrieved after the identity change
- **THEN** its stored scientific version, result bytes, formula evidence, and value remain unchanged
- **AND** no retired calculator is invoked to regenerate or convert the result

### Requirement: Use one explicit dynamic window policy for both BrainBeat variants

Classic and individualized BrainBeat SHALL default to a 20-second dynamic
window and one-second refresh step, support complete windows of 10, 20, and
30 seconds, and use a 10-second minimum. They SHALL NOT emit numeric warm-up
results before a complete selected window exists. Each window SHALL use the
shared four-second Hann Welch segments with 50% overlap and shared spectral
quality gate; the outer window policy SHALL NOT alter Welch parameters.

#### Scenario: Emit complete BrainBeat windows every second
- **WHEN** playback reaches 20, 21, and 22 seconds with a 20-second window
- **THEN** the analysis windows are 0–20, 1–21, and 2–22 seconds
- **AND** no numeric result is published before the first complete window

#### Scenario: Resolve the same policy in both execution paths
- **WHEN** Official Run or on-demand review omits BrainBeat window settings
- **THEN** both use the selected variant's 20-second window and one-second step
- **AND** explicit supported window selections are preserved

### Requirement: Retain shared spectral quality and preprocessing evidence

Both BrainBeat variants SHALL use shared analysis preprocessing and Welch
spectral estimation, including explicit Fz/Pz role order, the configured
optional 50/60 Hz notch, a 1–50 Hz analysis bandpass, and the shared 75%
minimum clean-segment ratio. A failed quality gate SHALL produce a null result
with quantitative quality evidence and rejected-segment reasons. Missing or
non-finite samples SHALL be rejected rather than imputed.

#### Scenario: Reject a low-quality paired window
- **WHEN** fewer than 75% of the window's Welch segments pass shared quality rules
- **THEN** the BrainBeat value is null with a quality-gate failure and segment counts
- **AND** neither variant falls back to a previous window's value

#### Scenario: Record preprocessing and spectral identity
- **WHEN** a BrainBeat result is persisted
- **THEN** provenance identifies source channel order, sample rate, notch choice, filter contract, actual window, Welch segment and overlap, quality, and scientific version
