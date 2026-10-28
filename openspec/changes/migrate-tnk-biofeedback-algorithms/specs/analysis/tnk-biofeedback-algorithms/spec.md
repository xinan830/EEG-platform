## Purpose

Provide separately identified, reproducible offline versions of the ANT-Amplify biofeedback scientific calculations inside brain-platform's official algorithm catalog, without changing existing platform algorithms or implying real-time acquisition support.

## ADDED Requirements

### Requirement: Register ANT-derived calculations as distinct internal TNK modules

The system SHALL implement each migrated ANT-derived scientific calculation as an independently owned module with a stable TNK-prefixed identity. Each module SHALL own its scientific definition, typed configuration, channel roles, output schema, quality rules, evidence, and explicitly current scientific version. The public official-algorithm catalog SHALL expose one selectable `tnk_biofeedback` group entry; child module identities SHALL remain internal to dispatch, evidence, and provenance, and SHALL NOT appear as independently selectable catalog entries. Existing platform algorithm identities and calculations SHALL remain unchanged. PyQt presentation, acquisition drivers, device control, and BDF recording are outside this capability.

#### Scenario: Discover the TNK algorithm group
- **WHEN** a client requests the official algorithm catalog
- **THEN** exactly one selectable TNK entry appears as `tnk_biofeedback` with group-level modes, role mapping, output units, parameters, and availability
- **AND** its metadata describes child modules and their required roles without making those children selectable
- **AND** existing platform algorithm entries retain their existing identities and definitions

### Requirement: Preserve ANT raw time-series semantics for the three Fz metrics

The system SHALL implement Crimson meditation, Zenlite calmness, Zenlite awareness, and Zenlite spectral relaxation as internal TNK children backed by an explicit raw time-series source. The source SHALL declare recording/montage identity, half-open sample bounds, sampling rate, channel order, V/float64 units, and a reject-on-missing-sample policy. The modules SHALL own their fixed resampling/filter/FFT/model semantics and SHALL NOT consume the platform PSD as a substitute.

#### Scenario: Run the raw-Fz children through the public group
- **WHEN** a dynamic `tnk_biofeedback` request has an explicit Fz role mapping
- **THEN** the group dispatches the four raw-Fz children through the time-series source while preserving the other child modules' spectral source contract
- **AND** the child IDs appear only in group child metadata, evidence, and provenance
- **AND** a missing Fz source, invalid bounds, non-finite samples, or an unavailable model produces a structured unavailable result for that child without fabricating zero

#### Scenario: Validate the migrated model artifacts
- **WHEN** the Crimson or Zenlite model is loaded
- **THEN** the asset SHA-256, input/output shape, and float32 dtype are checked before inference
- **AND** a validation or optional-runtime failure is reported as `TNK_MODEL_UNAVAILABLE`
- **AND** the backend records the model identity and target sample rate in child evidence

#### Scenario: Preserve the frozen legacy spectral relaxation formula
- **WHEN** Zenlite spectral relaxation is computed
- **THEN** the implementation uses the 1200-sample 256 Hz FFTPACK adjacent-pair amplitude layout, bands 0.5–4, 4–8, 8–12, 12–22, 22–32, and 32–56 Hz, the alpha/(low-beta+high-beta) ratio, the original `(ratio-0.1)/0.8*100` mapping, clipping to 0–100, and ten-point smoothing
- **AND** it does not silently reuse TNK IAPF, Welch PSD, or the platform relaxation formula

#### Scenario: Request unavailable TNK science
- **WHEN** a Run requests a TNK identity that is not registered or is unavailable for its input contract
- **THEN** the backend returns a structured error before persisting a successful result
- **AND** it does not substitute a same-named platform algorithm

### Requirement: Restrict the public TNK group to dynamic analysis

The public `tnk_biofeedback` catalog entry SHALL declare `dynamic` as its only supported analysis mode. A new Run request for that group with `mode=static` SHALL be rejected before any scientific execution using the runtime's structured unsupported-mode response. The system SHALL NOT convert the request into a dynamic Run. Existing persisted static TNK Run records SHALL remain readable using their stored result and provenance, but SHALL NOT be re-executed or migrated.

#### Scenario: Reject a new static TNK request
- **WHEN** a caller submits `tnk_biofeedback` with `mode=static`
- **THEN** the runtime returns a structured unsupported-mode error before loading or computing scientific input
- **AND** it does not create a dynamic substitute or overwrite an existing Run

### Requirement: Preserve the declared ANT-derived scientific semantics

Each TNK result SHALL identify the exact source definition, frozen parameters, input channels and order, sampling rate, preprocessing/reference snapshot, time/sample bounds, quality policy, algorithm version, and implementation identity. Units SHALL be explicit. Missing channels, invalid bands, non-finite inputs, failed quality gates, and insufficient data SHALL produce unavailable values with structured reasons rather than numeric zero. Numerical parity SHALL be assessed against independent references and SHALL NOT be described as clinical validation.

#### Scenario: Reproduce a valid reference calculation
- **WHEN** an eligible input satisfies a TNK module's frozen scientific contract
- **THEN** its output and intermediate evidence match the independent reference within that module's declared tolerances
- **AND** the Run records sufficient provenance to replay the calculation

#### Scenario: Reject invalid or insufficient input
- **WHEN** required channels, frequency coverage, minimum duration, signal quality, or numerical validity requirements are not met
- **THEN** the affected output is unavailable with a stable reason and quantitative quality evidence
- **AND** no unavailable output is replaced by zero or a different channel's result

### Requirement: Make history-dependent calculations deterministic across offline strategies

History-dependent TNK calculations SHALL declare their reset point, state-update order, warm-up, rolling-window bounds, lock/update cadence, smoothing, and output behavior. Full dynamic analysis SHALL process canonical windows in sample order. On-demand analysis SHALL replay the same required preceding context from the declared reset point and return the target window result. Combined execution SHALL reuse results only when complete context identity, algorithm identity, config, preprocessing, source, and target bounds match; otherwise it SHALL compute independently. No TNK offline Run SHALL be represented as live streaming output.

The IAPF family SHALL return `TNK_CONTEXT_WARMUP` before 30 seconds of reset-anchored data are available, then evaluate exactly the trailing 30-second window only at five-second cadence. A change to this schedule SHALL change the group implementation and dynamic-window-policy identities so results generated under the earlier schedule cannot be reused as current results.

BrainBeat Flat SHALL fit each current four-second Fz/Pz PSD independently over 3–30 Hz before calculating the residual spectrum. This fit is independent from the aperiodic child module's 30-second/5-second schedule. Flat SHALL report each fit's current source range and fit-quality evidence, and SHALL remain unavailable if either role's current-window fit fails. Only windows with both valid fits and valid residual powers SHALL advance Flat's independent warm-up and EMA state.

#### Scenario: Compute Flat before the aperiodic module's first scheduled fit
- **WHEN** a valid four-second BrainBeat Flat window occurs before 30 seconds of reset-anchored data are available
- **THEN** Flat fits the current Fz/Pz PSD itself and calculates its residual powers
- **AND** its evidence identifies the current source range of each fit
- **AND** its EMA warm-up advances only for valid Flat calculations

#### Scenario: Reject an incomplete current-window fit
- **WHEN** either Fz or Pz has no valid current-window fit
- **THEN** Flat returns a structured unavailable result without reusing another window's fit or advancing EMA state

#### Scenario: Request a stateful target window on demand
- **WHEN** an on-demand cursor targets a window for a stateful TNK algorithm
- **THEN** the backend deterministically evaluates the declared preceding context and state transitions required for that target
- **AND** the target result has the same value and evidence as the matching full dynamic window under identical context

#### Scenario: Run combined mode with context-sensitive results
- **WHEN** full and on-demand analysis overlap for a history-dependent TNK algorithm
- **THEN** the orchestrator reuses or joins results only when the entire declared context identity matches
- **AND** a context mismatch causes independent computation or a structured unsupported-capability result, never silent reuse

### Requirement: Reuse bounded TNK on-demand replay checkpoints

The TNK group's on-demand executor SHALL reuse module-state checkpoints created from the same immutable dynamic session identity. When a target follows a previously computed target, it SHALL resume from the nearest valid checkpoint at or before that target and process only later canonical windows. Checkpoints SHALL be isolated by source, recording, configuration, implementation, preprocessing, and reset/context identity; they SHALL be bounded in memory and safe under concurrent requests. A cache miss SHALL preserve deterministic replay from the declared reset origin. Within one window, identical channel/range spectral requests from independent child modules SHALL share the loaded spectrum without changing child formulas, quality gates, evidence, or result values. The executor SHALL expose actual replay work in diagnostics while keeping the scientific context range anchored to the original reset point.

#### Scenario: Continue sequential on-demand playback
- **WHEN** the next one-second target is requested in the same TNK dynamic session
- **THEN** the group restores the latest compatible state checkpoint and computes only the new canonical frame
- **AND** its values, child evidence, quality, and state equal the corresponding full-dynamic frame

#### Scenario: Jump to an uncached target
- **WHEN** a target is requested with no compatible earlier checkpoint
- **THEN** the group replays from the declared reset origin and stores bounded compatible checkpoints as frames complete

#### Scenario: Prevent checkpoint cross-contamination
- **WHEN** the source, channel mapping, filters, configuration, implementation, or context identity differs
- **THEN** no checkpoint or cached window spectrum from the other identity is restored
- **AND** deterministic replay starts from the new session reset point

### Requirement: Keep TNK result shapes and strategy capabilities explicit

Each TNK module SHALL declare whether it produces scalar, multi-metric, spectral, or series results and which full, on-demand, and combined strategies it supports. Arrays and large spectral evidence SHALL use controlled artifacts with checksum and shape metadata; Run summaries SHALL contain bounded values and artifact references. Strategy selection and scheduling SHALL remain centrally managed while all TNK scientific calculations remain module-owned.

#### Scenario: Render a migrated result
- **WHEN** WPF displays a completed TNK Run
- **THEN** it renders backend-returned values, units, quality states, ranges, and artifact-backed arrays without recomputing science

#### Scenario: Request an unsupported strategy
- **WHEN** a requested execution strategy is not declared by the TNK module
- **THEN** the backend returns a structured unsupported-capability reason and does not silently substitute another strategy

### Requirement: Resolve one canonical TNK mapping before child execution

The backend SHALL accept one typed TNK group mapping snapshot with canonical roles `Fz`, `Pz`, `Oz`, `F3`, and `F4`. All five are required because the current group runs every child module, including FAA (F3/F4) and RBP (all five roles). A child module SHALL declare the subset of roles it requires; the coordinator SHALL resolve those roles to source channels before loading data and SHALL reject missing, duplicated, or unavailable mappings before scientific execution. The coordinator SHALL not rename or infer roles from channel order. The public group catalog SHALL expose child identities, display names, and required role metadata for desktop presentation while keeping child identities internal to execution.

#### Scenario: Run IAPF from a shared mapping
- **WHEN** a TNK group Run maps `Pz` and `Oz` to valid source channels
- **THEN** the IAPF child receives those two prepared role inputs automatically
- **AND** the user is not required to configure an IAPF-specific secondary channel

#### Scenario: Reject incomplete group mapping
- **WHEN** a selected child requires a role whose group mapping is missing or unavailable
- **THEN** the group Run fails before child calculation with a structured missing-role reason
- **AND** no other child receives a substituted channel
