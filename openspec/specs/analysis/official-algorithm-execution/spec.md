# analysis/official-algorithm-execution Specification

## Purpose
在可追溯 Run 管线中执行已验证的官方 IAPF 与个体化 Theta/Beta，
同时保留明确逻辑通道映射、质量门和既有离线频谱数学契约。
## Requirements
### Requirement: Run official IAPF and individualized Theta/Beta

The system SHALL execute official IAPF, Theta/Beta, RBP and FAA through their
own registered backend modules and the existing Run lifecycle. Validated montage inputs
SHALL include persisted explicit analysis snapshots as well as acquisition snapshots;
their source and immutable content identity SHALL remain traceable. Algorithm
modules SHALL declare their client-safe inputs, including historical montage
and output-channel mapping, output unit/shape, time semantics and quality
behavior. The backend SHALL project the selected montage in V/float64 from
immutable recorded source channels before algorithm preprocessing. Each module
SHALL retain its own mathematical formula and quality rules.

#### Scenario: Select an executable official algorithm

- **WHEN** a client selects a catalog item marked runnable and a validated
  historical montage output
- **THEN** it submits only the parameters declared by that item's backend
  schema
- **AND THEN** the Run records the actual montage, source mapping, output
  evidence and module identities

#### Scenario: Preserve a specialized algorithm contract

- **WHEN** RBP, FAA, IAPF or Theta/Beta execute under a selected montage
- **THEN** each algorithm retains its own mathematical formula and quality
  rules over the projected signal
- **AND THEN** the shared runtime does not coerce multi-value RBP or paired FAA
  into an unrelated single-channel scalar contract

#### Scenario: Static IAPF

- **WHEN** a valid recording, one selected historical montage output, and
  analysis range are submitted for iapf
- **THEN** the completed Run returns IAPF in Hz, the selected resolved output,
  spectral evidence, quality, algorithm identity, and an Artifact

#### Scenario: Dynamic Theta/Beta

- **WHEN** a valid recording, one selected historical montage output, and
  dynamic theta_beta configuration are submitted
- **THEN** the Run returns real window bounds and one individualized ratio per
  refresh point without fabricating values for rejected windows
- **AND THEN** every point's trend time is the end of its actual analysis
  window, not the window centre

#### Scenario: Dynamic warm-up before the selected analysis window fills

- **WHEN** playback has reached at least one complete 4 s Welch segment but
  less than the selected dynamic analysis-window duration
- **THEN** the Run evaluates the available real EEG range and returns the
  result as an explicitly marked warm-up point
- **AND THEN** once the configured analysis window is available, subsequent
  points use the fixed trailing window ending at the playback time

#### Scenario: Inspect a dynamic point

- **WHEN** a completed dynamic official Run is opened in the read-only debug
  workbench
- **THEN** its latest point includes backend-returned sampling rate, filter,
  Welch, frequency-axis, PSD, and quality evidence for that exact window

#### Scenario: Upgrade the persisted dynamic-result contract

- **WHEN** the official dynamic-result evidence or time-anchor contract changes
- **THEN** the Run cache identity changes with it
- **AND THEN** a result persisted under the prior contract is retained only as
  historical evidence and is never reused as the upgraded result

#### Scenario: Missing logical mapping

- **WHEN** Theta/Beta is requested without a global semantic mapping but with a
  validated explicitly selected montage output
- **THEN** the Run uses the explicitly selected resolved output
- **AND THEN** it does not return OFFICIAL_CHANNEL_MAPPING_REQUIRED or infer a
  channel by position or alias

#### Scenario: Dynamic Theta/Beta on one source channel

- **WHEN** a valid recording, one selected historical montage output sourced
  from O2, and dynamic Theta/Beta config are submitted
- **THEN** each returned point records its actual window range and reports one
  Theta/Beta value sourced from the resolved O2 output
- **AND THEN** the system does not relabel O2 as Oz, compute Fz/Pz/Oz role
  outputs, or average any channels outside the selected montage formula

#### Scenario: Unavailable dynamic point

- **WHEN** a dynamic window fails the PSD quality gate, lacks IAPF, or has an
  invalid Beta denominator
- **THEN** that point's value is null with a stable backend reason and is
  never represented as zero

### Requirement: Catalog state matches executable scope

The official catalog SHALL derive availability and runnable state from each
installed validated runtime module rather than an obsolete algorithm-name
allowlist. IAPF, Theta/Beta, FAA and offline Brainbeat SHALL be available and
runnable when their corresponding validated modules are installed. This
offline capability SHALL NOT enable a stateful realtime Brainbeat path.

#### Scenario: Read catalog after the cutover

- **WHEN** a client requests the official catalog with the validated IAPF, Theta/Beta, FAA and offline Brainbeat modules installed
- **THEN** those entries are available and runnable for their declared modes
- **AND** each reports its module-owned scientific and implementation identity
- **AND** offline Brainbeat availability does not claim realtime IAPF-lock or EMA support

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

### Requirement: PSD exposes standard band shares in structured results
The official PSD structured result MUST expose `band_share` in the fixed order
Delta, Theta, Alpha, Beta, Gamma, with unit `ratio`. Static output MUST use
five values and dynamic output MUST use one five-value row per analysis window.

#### Scenario: Render band shares without client recomputation
- **WHEN** a clean PSD run completes
- **THEN** the structured preview contains `band_share`
- **AND** its values are backend-computed relative powers
- **AND** the calculation trace declares the band names and ranges

### Requirement: Isolate algorithm spectral preprocessing from display filters

Official spectral algorithms SHALL receive analysis preprocessing settings through their algorithm request/configuration contract. Review waveform filter controls SHALL NOT modify algorithm inputs, preprocessing, run identity, cache identity, or result provenance. The shared spectral baseline SHALL cover 1–50 Hz with a default 50 Hz notch, while each algorithm retains its declared analysis window, Welch segmentation, target frequencies, bands, and quality rules. Compatible user-facing algorithm configurations SHALL permit an explicit notch-off choice.

#### Scenario: Display filter changes during algorithm analysis
- **WHEN** the user changes review waveform filters and submits an official spectral run
- **THEN** the run uses its algorithm-owned preprocessing configuration
- **AND** the display filter values do not appear in its request or provenance.

#### Scenario: Compare notch-on and notch-off runs
- **WHEN** a user executes the same compatible algorithm and range once with default notch and once with notch disabled
- **THEN** each run records its own effective preprocessing settings and distinct run identity
- **AND** each preserves the algorithm's window, Welch parameters, target range, and quality policy.

#### Scenario: Preserve algorithm-specific analysis window
- **WHEN** two spectral algorithms use different declared window lengths or Welch parameters
- **THEN** the shared spectral service computes each request with its declared values
- **AND** the shared preprocessing baseline does not overwrite either configuration.
