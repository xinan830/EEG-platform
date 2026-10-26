## Context

See proposal.md for motivation and specs for observable contracts. The source project is `ANT-Amplify/eeg-process-main`; its implementation is split mainly between `src/processing/processor.py` and `src/processing/iapf_estimator.py`. The target platform has a closed registry of nine current official modules. Several names overlap, but the user chose to preserve those modules and add separately identified TNK algorithms.

## Goals / Non-Goals

**Goals:**
- Port the scientific calculations and result evidence, not the ANT application shell or acquisition lifecycle.
- Keep formulas and per-algorithm policies in independently testable backend modules.
- Use the platform's central catalog, Run lifecycle, strategy orchestration, provenance, artifact, and WPF presentation boundaries.
- Make history-dependent offline outputs deterministic and comparable across full, on-demand, and combined execution.

**Non-Goals:**
- Port PyQt widgets, LSL/eego/USB drivers, experiment controls, or ANT BDF recording.
- Change any existing platform algorithm or claim clinical validity.
- Introduce a real-time/streaming Runtime in this change.

## Decisions

1. **Inventory before implementation.** Freeze an algorithm-by-algorithm source ledger, distinguishing user-visible calculations from shared preprocessing and presentation. The audited candidate inventory is TNK-IAPF; Relaxation; Spatial Distribution; Rhythm Stability; Composite Meditation Index; BrainBeat; BrainBeat Flat (1/f-residual); per-channel Fatigue; HAI; FAA; fixed-band RBP; and aperiodic exponent (“cortical excitation/inhibition” display). Residual-spectrum RBP is excluded: its panel, processor path, and payload are explicitly asserted removed by a focused test. Confirm each remaining candidate's executable path and tests before registration. The display-only aperiodic offset is evidence, not a separate algorithm; the composite's numerical aggregation is in scope, while UI smoothing, trend statistics, and randomized startup display values are not.

2. **Stable TNK identities, no replacement.** Existing platform IDs keep their current scientific version and implementation. Every migrated formula gets a distinct `tnk_` ID and TNK display prefix, even when its name overlaps. No same-name fallback, alias, compatibility calculator, or rewriting of old Run data is added.

3. **Algorithm-owned science, centrally managed execution.** Each TNK module owns its manifest, typed config, role/channel validation, calculation, output schema, quality policy, evidence, and strategy declaration. Shared Run/queue/runtime code handles lifecycle and generic scheduling only. Generic services must not branch on TNK IDs.

4. **Separate mathematical primitives from composed metrics.** Reuse platform spectral primitives only where their declared frequency axis, units, Welch details, window bounds, preprocessing, and quality semantics exactly match the ANT reference. Otherwise implement the required calculation inside the owning TNK module using existing numerical dependencies; do not silently redefine shared primitives or reuse a same-named official algorithm as a calculator.

5. **Deterministic offline state semantics.** Each Run has a sample-index reset origin; any distinct segment/phase origin is explicit in config and provenance. Windows use half-open sample ranges on a reset-anchored canonical grid, independent of source chunking. Dynamic 4 s windows use 1 s ticks after the 4 s window plus 3 s filter-settle gate; the IAPF family uses a separate 30 s rolling input and 5 s cadence; segment reports use explicit segment bounds. A target on-demand request evaluates the module-declared transitions from the same reset/segment origin, applies warm-up/lock/history state in a fixed order, and publishes only the requested target result. A held source value is evidence, not a newly computed target. Exact session-identity checkpoints may resume that deterministic evaluation; a cold cache replays from reset. Cache identity includes reset boundary and context/config/source identity. Combined reuse is declared independently per direction and is allowed only for exact context identity. Per-module reset and cadence details are frozen in `docs/algorithms/tnk-source-ledger.md` section 2.1.

   BrainBeat Flat retains the latest valid aperiodic fit independently for Fz and Pz between scheduled aperiodic refreshes. It applies that fit to each current Flat PSD, advances Flat warm-up/EMA only when the current fit pair and residual powers are usable, records the fit's source bounds and quality, and clears cached fits on reset. The existing child-state checkpoint therefore carries these fits through deterministic on-demand replay.

6. **Do not force false parity for non-stateful and stateful algorithms alike.** Stateless metrics (e.g. HAI per PSD window) can use the shared independent-window capability. Stateful metrics (IAPF candidate locking, BrainBeat/Fatigue warm-up and EMA, Rhythm Stability history) either replay context deterministically or declare a narrower strategy with an explicit unsupported reason. The plan must not label a window-local recomputation as parity with ANT's stateful output.

7. **Source parity and numerical references.** Build independent reference code from frozen ANT formulas and pinned environment metadata. Compare frequency axes, PSDs/intermediate powers, values, quality decisions, state transitions, and provenance on analytic synthetic inputs; use any separately authorized de-identified recording only as supplementary evidence. Preserve source attribution and tolerances per algorithm.

8. **Incremental catalog and desktop integration.** Register each completed module once in the built-in composition boundary. WPF uses backend catalog metadata and module-owned result handlers/previews. No scientific formula or hidden ANT-specific scheduling is placed in WPF or generic Run services.

10. **TNK is one user-facing algorithm group with independent scientific children.** The catalog exposes one current `tnk_biofeedback` group entry. Its configuration owns a canonical role-to-source mapping for `Fz`, `Pz`, `Oz`, `F3`, and `F4`; all five roles are required because the fixed group includes FAA and per-role RBP. The group validates and persists that snapshot once. A group coordinator dispatches only the roles required by each independently implemented child module, and aggregates child results without moving formulas into the coordinator. The catalog declares each child's role requirements for desktop display. Users do not configure `secondary_channel` or `tertiary_channel` per child. The child identities remain internal current modules for tests, provenance, and result ownership; they are not separate algorithm-list entries.

9. **Output/storage policy.** Compact Run summaries contain scalar/window states and evidence metadata. Large spectra or arrays are stored as controlled artifacts with checksum, shape, unit, and provenance. Any new output family or persistence/API change must be justified against current contracts before implementation.

11. **Runtime replay identity and budgets.** The generic on-demand session/window identity carries an explicit reset sample and context identity; plans carry a half-open replay context range and replay-window count in addition to the target range. Cache keys and published provenance include that replay identity. The scheduler remains algorithm-agnostic. Shared resource policy applies sample and window budgets to replay and emits a structured `WINDOW_RESOURCE_EXCEEDED` failure with requested and allowed amounts; modules cannot silently truncate history. The TNK strategy/reuse decisions are recorded per algorithm in `docs/algorithms/tnk-strategy-matrix.md`; these are intended contracts, not implemented/validated capability claims.

12. **Bounded incremental TNK on-demand execution.** The group adapter owns a process-local, bounded checkpoint cache keyed by the complete immutable `DynamicSessionIdentity`. A checkpoint stores deep-copied child adapter state after a canonical frame; target execution restores the nearest earlier compatible checkpoint, replays only subsequent frames, and writes checkpoints only after each completed frame. Cache misses retain reset-origin replay. The context range in provenance remains reset-origin-to-target even when execution work resumes from a checkpoint; replay-work counts and checkpoint origin are logged separately. A request-local spectrum cache inside one frame reuses only identical channel/sample-range spectra across children. No scientific state or formula moves into shared schedulers/services, and cache eviction only increases replay work.

13. **TNK is dynamic-only at its public boundary.** The `tnk_biofeedback` manifest declares only `dynamic`; WPF selects dynamic mode when this group is chosen and does not show a static-mode action. The generic runtime rejects new static TNK requests before execution with its structured unsupported-mode contract. Persisted historical static TNK Runs remain readable through the existing result readers, but no compatibility executor, conversion, or implicit dynamic substitution is added.

## Risks / Trade-offs

- TNK workbench: reproduce the original ANT waveform / bottom chart tabs / right controls. Module-owned projections group the independent returned children into eight chart families. RBP uses one four-color SciChart stacked trend per channel, ordered delta/theta/alpha/beta, with ratio-to-percent formatting only; other metrics use time curves. A single window appears as points at actual recording seconds. A session-isolated display history keeps at most 600 returned windows, replaces duplicates and inserts null gaps for uncomputed intervals; it does not calculate missing values. The shared waveform reuses review plot/label geometry, fixed µV spacing and the shared time formatter. SciChart owns zoom, pan, reset and tooltips; cursor and drag release link to recording seconds. A 33 ms display timer and one-second analysis targets have separate cadence. Source changes withhold old results until the matching recomputation.
- Waveform source: typed POST `/api/recordings/{id}/signal-preview` selects the immutable montage, validates low/high/notch settings, reuses the continuous zero-phase scientific preparation cache, then crops up to 30 seconds / two million sample-values. Preparation may read the whole recording, uses mapped buffers with a 512 MiB budget and disk guard, and is not misrepresented as a window-only read. Returned data declares µV, seconds, filters and display stride. Cancellation and revision checks reject stale bindings. Stride sampling may miss brief peaks and is display-only. TNK scientific execution remains fixed 1–30 Hz; display filtering does not alter that definition.
- Sidebar: event annotation/report controls and event API calls are excluded at the user's explicit request. GET `/api/runs/{id}/tnk-workbench?cursor_s=...` returns typed cumulative recording-time state statistics from a completed TNK Run. Thresholds 55/65/75 and colors are taken from ANT `eeg_biofeedback.py`, not guessed from pixels. Missing windows break duration accumulation. The client fetches the immutable timeline once per Run and only selects frames during playback.

- [History replay can make a single on-demand request expensive] → Measure context length and bound it only where the ANT definition permits; return explicit resource/unsupported reasons rather than truncate history silently.
- [ANT source documentation can diverge from executable code] → Treat source code and focused tests as evidence, record disagreements, and freeze a per-algorithm decision before porting.
- [ANT and platform preprocessing differ] → Preserve the TNK-specific preprocessing contract and expose it in provenance; never claim same-name algorithms are numerically interchangeable.
- [Many algorithm modules increase catalog and UI surface] → Migrate in dependency-ordered groups and require per-module completion gates before registering the next group.
- [The source application uses a continuous stream while target execution is offline] → Define deterministic run reset and context semantics; do not describe offline replay as real-time equivalence.
- [Spectral arrays can grow result storage] → Keep summaries bounded and use controlled artifacts with explicit checksums and size accounting.

## Migration Plan

1. Freeze the source inventory and each ANT scientific contract; document overlaps and discrepancies.
2. Define shared support for deterministic module context replay and prove it with a small stateful example before algorithm migrations.
3. Port stateless window calculations first, then stateful/history-dependent calculations, preserving separate TNK identities throughout.
4. Add backend catalog registration, controlled evidence/artifacts, and WPF catalog-driven handlers per completed group.
5. Validate against independent source references and analytic signals; run desktop and strategy acceptance before marking each group complete.

Existing platform algorithms and stored data are not migrated. Rollback consists of marking the affected current TNK catalog entries unavailable and reverting the change; no legacy TNK execution path is retained.

## Remaining Decisions

- Task 1.2 froze ANT observable source semantics and comparison tolerances. The chosen offline reset boundary and deterministic replay behavior are specified by task 2; implementation must preserve sample-aligned source cadence and must not claim live-stream equivalence.
- Task 1.4 still needs independent analytic fixtures and final reference identity metadata. The ANT source test pass in the locked environment is evidence about source behavior only, not numerical parity evidence for migrated modules.
- Platform-side missing-channel and unavailable/held quality mapping is governed by the current Run contract and the TNK module requirements; it must not copy ANT UI's zero/stale-value presentation semantics.
