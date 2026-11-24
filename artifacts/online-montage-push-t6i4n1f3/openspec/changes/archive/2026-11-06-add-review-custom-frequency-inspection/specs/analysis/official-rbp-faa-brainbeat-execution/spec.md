## MODIFIED Requirements

### Requirement: Execute official RBP as one five-band result

The system SHALL execute current standalone `rbp` on explicitly selected ordered channels in static or dynamic mode and return Delta 1–4, Theta 4–8, Alpha 8–13, Beta 13–30 and Gamma 30–50 Hz relative powers from channel-independent 1–50 Hz PSDs. Each power SHALL use boundary-aware integration; the denominator SHALL be the sum of all five powers. Source PSD evidence SHALL declare reference, continuous 1–50 Hz bandpass, 50 Hz notch Q=30, Welch settings, frequency grid and units. Gamma SHALL be identified as filtered power. Nyquist frequency MUST exceed 50 Hz. Current execution SHALL use `official-rbp-v4`, `rbp-runtime-v9-private-projection-source` and Definition `2.0.9`, with on-demand policy `rbp-dynamic-window-v4-custom-spectrum`, without rewriting historical Runs or adding retired execution fallbacks.

Finite artifact-bearing segments SHALL participate in standalone RBP Welch with channel-specific amplitude, suspected-clipping and low-amplitude warnings, without a minimum clean-segment-ratio gate. Missing/non-finite samples and invalid five-band total power SHALL yield unavailable values for only the affected channel, without imputation or carried-over measurements. Values with warnings SHALL remain visible and SHALL NOT be described as artifact-free. Other algorithms' declared quality policies SHALL remain unchanged.

#### Scenario: Static RBP result
- **WHEN** valid channels and a static range are submitted
- **THEN** each computable channel returns five ratio shares summing approximately to one, band powers, actual range, channel quality and spectral evidence
- **AND** frontend displays backend values without recalculating or renormalizing

#### Scenario: Dynamic RBP result
- **WHEN** a dynamic window contains both healthy and artifact-bearing computable channels
- **THEN** values remain visible with independent channel warnings and the actual time bounds
- **AND** whole-window aggregate quality is partial whenever a channel has warnings or unavailable results

#### Scenario: RBP quality failure
- **WHEN** a channel window has missing/non-finite samples or an invalid denominator
- **THEN** that channel has unavailable shares and a structured reason while unaffected channels remain usable
- **AND** invalid recording sampling rates are rejected without falling back to four bands or fabricated values

## ADDED Requirements

### Requirement: Retain the existing on-demand RBP spectrum for custom projections

Current on-demand RBP windows SHALL retain their already computed shared `frequency_hz` axis in Hz and channel-ordered `spectral_psd` in `V^2/Hz` with shape `[channel, frequency]`. These fields SHALL be declared in the on-demand output contract without changing existing five-band arrays, static/full-series shapes, topomap fields, scientific formulas or quality policies.

#### Scenario: Inspect a current saved RBP window
- **WHEN** a matched PSD/RBP pair is used to inspect a bin or custom interval
- **THEN** the RBP-owned projection integrates the saved spectrum with the existing exact-edge trapezoid primitive, returns absolute power in µV², relative power in percent of the complete saved 1–50 Hz denominator, and equivalent RMS in µV
- **AND** it preserves channel quality and source identity without starting another scientific execution or spatial interpolation
- **AND** it reads the saved spectrum and saved complete five-band denominator without re-reading EEG or rerunning Welch

#### Scenario: Read a normal RBP window or reuse a custom source
- **WHEN** ordinary ensure/status, five-band values or topomap reads consume the window
- **THEN** backend-only spectral projection fields are neither decompressed nor returned to those consumers
- **AND** custom projections access the same immutable saved source only after project-scope validation, reuse bounded validated decoded data, and invalidate reuse when either artifact file or its repository manifest digest changes

#### Scenario: Inspect a historical result without spectral arrays
- **WHEN** an otherwise readable saved RBP result lacks the custom-frequency spectral source
- **THEN** the custom projection returns a structured unavailable response while existing five-band and topomap consumers keep their original behavior
- **AND** historical results are neither reconstructed nor migrated for this projection
