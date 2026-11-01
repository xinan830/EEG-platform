## MODIFIED Requirements

### Requirement: Execute official RBP as one five-band result

The system SHALL execute current standalone `rbp` on explicitly selected ordered channels in static or dynamic mode and return Delta 1–4, Theta 4–8, Alpha 8–13, Beta 13–30 and Gamma 30–50 Hz relative powers from channel-independent 1–50 Hz PSDs. Each power SHALL use boundary-aware integration; the denominator SHALL be the sum of all five powers. Source PSD evidence SHALL declare reference, continuous 1–50 Hz bandpass, 50 Hz notch Q=30, Welch settings, frequency grid and units. Gamma SHALL be identified as filtered power. Nyquist frequency MUST exceed 50 Hz. Current execution SHALL use `official-rbp-v4`, `rbp-runtime-v5-channel-quality` and Definition 2.0.4 without rewriting historical Runs or adding retired execution fallbacks.

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
