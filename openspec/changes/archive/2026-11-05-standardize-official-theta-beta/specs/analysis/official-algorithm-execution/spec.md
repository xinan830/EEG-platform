## MODIFIED Requirements

### Requirement: Preserve scientific contracts
Official IAPF and Theta/Beta SHALL reuse frozen offline spectral preprocessing, quality gates, units, and time-window semantics. IAPF SHALL estimate its value from the PSD averaged across accepted Welch segments in the current analysis window. Theta/Beta SHALL support two explicit frequency modes on the same selected raw channel: standard mode SHALL use Theta `[4,8]` Hz and Beta `[13,30]` Hz; individualized mode SHALL estimate IAPF for the current window and use Theta `[IAPF-6,IAPF-4]` Hz and Beta `[IAPF+2,30]` Hz. The lower Theta edge SHALL NOT be clamped. An unavailable IAPF or band outside the actual frequency axis SHALL produce a null result with a structured reason. The default frequency mode SHALL be standard.

#### Scenario: IAPF uses averaged window PSD
- **WHEN** a complete dynamic IAPF window contains multiple quality-accepted Welch segments
- **THEN** the backend averages those segment PSDs before fitting the aperiodic background and estimating Alpha frequency
- **AND** it does not average per-segment IAPF estimates

#### Scenario: Standard Theta/Beta does not depend on IAPF
- **WHEN** standard Theta/Beta is requested for a valid spectrum
- **THEN** the backend integrates 4–8 Hz Theta and 13–30 Hz Beta power from the selected channel
- **AND** an unavailable IAPF does not prevent this result

#### Scenario: Individualized Theta/Beta uses the declared IAF-relative bands
- **WHEN** individualized mode returns a valid IAPF of 10 Hz
- **THEN** the backend integrates Theta from 4–6 Hz and Beta from 12–30 Hz
- **AND** it records the IAPF, effective bands, and ratio calculation evidence

#### Scenario: Match static and dynamic windows
- **WHEN** static Theta/Beta and a dynamic Theta/Beta point use the same mode, selected channel, and identical actual range
- **THEN** their IAPF when applicable, band powers, quality, effective bands, and ratio match within the algorithm validation tolerance

#### Scenario: Reject an unavailable IAPF
- **WHEN** the IAPF fit produces no accepted Peak or COG after the shared PSD quality gate passes
- **THEN** the output is `null` with its backend reason and is never zero

#### Scenario: Do not clamp individualized band edges
- **WHEN** individualized mode returns an IAPF of 8 Hz and the frequency axis covers 2–30 Hz
- **THEN** the backend uses Theta 2–4 Hz and does not shift the lower edge to 4 Hz

#### Scenario: Reject an unavailable or unintegrable individualized band
- **WHEN** IAPF is unavailable or a requested band is outside the actual frequency axis
- **THEN** the backend returns null with a structured reason and never emits zero as a substitute

#### Scenario: Match static and dynamic windows
- **WHEN** static Theta/Beta and a dynamic Theta/Beta point use the same mode, selected channel, and identical actual range
- **THEN** their IAPF when applicable, band powers, quality, effective bands, and ratio match within the algorithm validation tolerance

#### Scenario: Official IAPF rejects unsupported Alpha evidence
- **WHEN** neither PAF nor COG passes its declared Alpha-evidence criterion after the shared PSD quality gate passes
- **THEN** the backend returns null with an Alpha-estimate rejection reason and retains diagnostic evidence

#### Scenario: Reject an unavailable IAPF
- **WHEN** the IAPF fit produces no accepted Peak or COG after the shared PSD quality gate passes
- **THEN** the output is `null` with its backend reason and is never zero

### Requirement: Official IAPF and Theta/Beta dynamic results require complete analysis windows
Official IAPF and Theta/Beta dynamic policies SHALL default to a 20-second analysis window and one-second refresh step, support the declared 20- and 30-second windows, and SHALL NOT publish numeric warm-up values. Each complete window SHALL use the shared four-second Hann Welch segments with 50% overlap and the shared quality gate. Theta/Beta dynamic windows SHALL begin at the configured analysis start and advance by the selected step.

#### Scenario: Emit complete TBR windows every second
- **WHEN** a dynamic Theta/Beta run starts at 0 seconds and reaches 20, 21, and 22 seconds
- **THEN** it analyzes 0–20, 1–21, and 2–22 seconds respectively
- **AND** no numeric Theta/Beta value is emitted before the first complete 20-second window

#### Scenario: Emit the first complete IAPF window
- **WHEN** dynamic playback reaches 20 seconds from its configured start
- **THEN** IAPF is calculated from the 0–20 second window using its nine complete Welch segments
- **AND** no IAPF numeric value is published for earlier partial windows

#### Scenario: Advance complete IAPF windows every second
- **WHEN** playback reaches 21 and 22 seconds
- **THEN** IAPF uses windows 1–21 and 2–22 seconds respectively

#### Scenario: Advance complete windows every second
- **WHEN** playback reaches 21 and 22 seconds
- **THEN** the selected dynamic policy advances complete analysis windows by one second

#### Scenario: Average only quality-accepted Welch segments
- **WHEN** one or more Welch segments fail quality checks but the window still meets the shared minimum clean-segment ratio
- **THEN** only accepted segment PSDs are averaged before IAPF estimation
- **AND** when the ratio is below the shared threshold, the window is rejected with a null value

#### Scenario: Use the same window policy in Official Run and on-demand execution
- **WHEN** Official Run and on-demand requests omit TBR dynamic window settings
- **THEN** both resolve to the 20-second window and one-second step declared by the TBR module
- **AND** unsupported shorter windows are rejected with a structured configuration error
