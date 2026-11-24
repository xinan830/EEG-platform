## MODIFIED Requirements

### Requirement: Preserve scientific contracts
Official IAPF and Theta/Beta v2 SHALL reuse frozen offline spectral preprocessing, quality gates, units, and time-window semantics. IAPF SHALL estimate its value from the PSD averaged across accepted Welch segments in the current analysis window. Theta/Beta v2 SHALL calculate Theta over `[max(4, IAPF-6), IAPF-2]` Hz and Beta over `[IAPF+2,30]` Hz from the same selected channel.

#### Scenario: IAPF uses averaged window PSD
- **WHEN** a complete dynamic IAPF window contains multiple quality-accepted Welch segments
- **THEN** the backend averages those segment PSDs before fitting the aperiodic background and estimating Alpha frequency
- **AND** it does not average per-segment IAPF estimates

#### Scenario: Match static and dynamic windows
- **WHEN** static Theta/Beta and a dynamic Theta/Beta point use the same selected channel and identical actual range
- **THEN** their IAPF, band powers, quality, and ratio match within the algorithm validation tolerance

#### Scenario: Reject an unavailable IAPF
- **WHEN** the IAPF fit produces no accepted Peak or COG after the shared PSD quality gate passes
- **THEN** the output is `null` with its backend reason and is never zero

### Requirement: Official IAPF requires supported Alpha evidence
Official IAPF SHALL only return PAF or COG when the respective estimate passes its declared Alpha-evidence criterion. A positive residual alone SHALL NOT qualify an estimate. Rejected estimates SHALL return null with a stable reason and retain diagnostic evidence.

#### Scenario: Accept a prominent local Alpha peak
- **WHEN** the background-corrected 7–13 Hz spectrum contains a local peak that passes the configured relative prominence and minimum-width criteria
- **THEN** the backend returns that peak frequency as PAF and records the criterion measurements

#### Scenario: Use COG only with sufficient distributed Alpha excess
- **WHEN** no PAF passes but integrated positive Alpha residual is at least 20% of fitted Alpha background and its participation-ratio support spans at least three effective bins
- **THEN** the backend returns the residual-weighted COG and records its evidence separately from PAF

#### Scenario: Reject noise without supported Alpha activity
- **WHEN** neither PAF nor COG passes its criterion
- **THEN** the backend returns null with an Alpha-estimate rejection reason, even if numerical residuals are slightly positive

### Requirement: Official IAPF dynamic results require a complete analysis window
Official IAPF dynamic policy SHALL default to a 20-second analysis window and one-second refresh step. It SHALL NOT publish IAPF values from partial warm-up windows. For each complete window, it SHALL compute Welch PSD using the declared four-second Hann segments with 50% overlap, apply the shared quality gate, and average accepted segment PSDs before IAPF estimation.

#### Scenario: Emit the first complete IAPF window
- **WHEN** dynamic playback reaches 20 seconds from its configured start
- **THEN** IAPF is calculated from the 0–20 second window using its nine complete Welch segments
- **AND** no IAPF numeric value is published for earlier partial windows

#### Scenario: Advance complete windows every second
- **WHEN** playback reaches 21 and 22 seconds
- **THEN** IAPF uses windows 1–21 and 2–22 seconds respectively

#### Scenario: Average only quality-accepted Welch segments
- **WHEN** one or more Welch segments fail quality checks but the window still meets the shared minimum clean-segment ratio
- **THEN** only accepted segment PSDs are averaged before IAPF estimation
- **AND** when the ratio is below the shared threshold, the window is rejected with a null value
