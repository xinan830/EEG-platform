## ADDED Requirements

### Requirement: Publish RBP absolute band power as structured output
The current official RBP result SHALL publish each channel's absolute Delta, Theta, Alpha, Beta, and Gamma band power as structured arrays alongside the existing relative-power arrays. Each output array SHALL have the same channel order and window dimensions as its corresponding relative-power array and SHALL declare `uV^2`. Static, dynamic-series, and on-demand RBP results SHALL use the same output names and units. Absolute power SHALL be the numerator band power already computed from the current RBP PSD; this addition SHALL NOT change the five-band denominator, relative-power values, preprocessing, quality policy, or requested and actual time ranges. Unavailable channel values SHALL remain null/unavailable with the existing per-channel failure reason and SHALL NOT be replaced with zero.

#### Scenario: Static and on-demand RBP output
- **WHEN** a static or on-demand RBP window contains computable channels
- **THEN** each channel returns five absolute-power values in `uV^2` aligned with the existing five ratio arrays and channel order
- **AND THEN** a channel that is unavailable for relative RBP is also unavailable for the corresponding absolute-power values

#### Scenario: Dynamic RBP output
- **WHEN** a dynamic RBP run contains multiple windows
- **THEN** each absolute-power array has the same window-by-channel shape and time axis as its corresponding relative-power array
- **AND THEN** each window retains the existing quality state, failure details, and absolute file-time bounds

#### Scenario: Current result identity
- **WHEN** the expanded structured output contract is resolved for a new RBP result
- **THEN** its Definition and implementation identities distinguish it from results that only declare relative-power arrays
- **AND THEN** its scientific version remains `official-rbp-v4` because the formula and preprocessing are unchanged

### Requirement: Record request-correlated RBP window stage durations
For an on-demand RBP window, backend diagnostics SHALL report the request ID, window ID, recording ID, half-open sample range, spectrum-load duration, and band-integration duration. Existing source-preparation timing SHALL remain distinguishable from those algorithm stages. Diagnostics SHALL NOT include EEG samples or numeric output arrays and SHALL NOT affect the result, cache identity, or scientific version.

#### Scenario: Diagnose a slow RBP window
- **WHEN** an on-demand RBP window takes longer than expected
- **THEN** logs for the same request and window distinguish source preparation, spectrum loading, band integration, and result publication
- **AND THEN** the returned arrays, units, channel order, and window bounds are unchanged
