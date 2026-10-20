## Context

See proposal.md for motivation and specs for required behavior. The backend already has `AlgorithmManifest`, `AlgorithmModule`, a closed built-in module list and a registry. Official Definition assembly still contains algorithm-ID branches, while WPF separately registers UI descriptors and result handlers and exposes typed detail properties. Those are distinct responsibilities, but their completeness and metadata ownership are not checked as one release contract.

The requested version policy intentionally removes historical version compatibility. New Runs use one explicitly current version per official algorithm ID. No migration or UI/API support is promised for retired-version Runs/artifacts; old bytes are not intentionally deleted by this management change, but may become inaccessible in the current product.

## Goals / Non-Goals

**Goals:**

- Establish one authoritative backend module declaration for official identity, display/catalog metadata, parameter schema, capabilities and output contract.
- Keep formula, specialized channel roles, scientific preprocessing, quality extensions and numerical evidence inside each algorithm module.
- Make registration completeness across backend and WPF deterministic and testable.
- Make new-algorithm onboarding executable as a checklist with named declaration/registration locations, required tests and evidence artifacts; automated checks report the exact missing item and block release readiness.
- Preserve separate implementation and validation states for each strategy and reuse direction. Passing independent full and on-demand tests is insufficient evidence for combined-mode validation.
- Maintain a traceability chain from the current module declaration and implementation identity through frozen Run configuration, selected strategy, result provenance, tests, and validation evidence. Retired-version records may remain physically stored without current-client read compatibility, but their stored identity fields must remain interpretable as historical identifiers.
- Manage full, on-demand, and combined dynamic strategies through explicit per-algorithm declarations, while reusing the shared scheduler/orchestrator already in the codebase. Declare each reuse direction independently and only where that algorithm's output and identity contract support it.
- Make current-version selection explicit, with no implicit latest sorting and no retired-version execution, migration, interpretation, or display compatibility layer.
- Preserve current scientific outputs and public response behavior during management consolidation unless a separate approved contract change says otherwise.

**Non-Goals:**

- Rewriting or combining scientific calculations, runners, or algorithm-specific output projections.
- Requiring every algorithm to support all three dynamic strategies or to reuse results across strategies when scientific identity/window semantics do not match.
- Building one cross-language runtime registry shared by Python and C#.
- Changing formulas, filters, units, channel mappings, quality thresholds, windows, or API semantics.
- Deleting stored recordings, Runs, artifacts, or database rows.
- Preserving access to or interpreting Runs/artifacts from retired scientific identities.

## Decisions

1. **Backend module declaration is the authoritative management source.** Extend or compose typed manifest metadata only where it describes identity, safe catalog parameters, modes, output, and registration requirements. Algorithm-specific science stays in module config/runner/compute/evidence. Build the public catalog from the closed runtime modules. Remove duplicated formula/contract branches from catalog assembly only after proving they are derivable without losing Definition provenance.

   Alternative: keep a second static official catalog because it is convenient for Definition creation. Rejected as a long-term source of truth because it can drift from executable manifests. Immutable Definition persistence may remain as a storage/provenance concern, but its draft must be deterministically derived from the authoritative module declaration and generic adapters rather than a growing algorithm-ID switch.

2. **Use separate registries per process boundary, one contract across them.** Python remains the scientific authority; WPF keeps its typed UI descriptors and result handlers because it owns different code and presentation behavior. A completeness test joins identities/capabilities/output schemas from API catalog fixtures with required WPF registrations and fails when an integration is missing. Do not share executable Python/C# registration code.

   Alternative: move every desktop mapping into one giant registry or generate C# source from Python manifests. Rejected initially because the WPF page factories and preview handlers carry UI-specific types and behavior; code generation would add build coupling and obscure ownership.

3. **Unify capability management; do not rebuild the strategy engine.** The code already has shared full/on-demand/combined orchestration and a common dynamic-window identity/store. The gap is that strategy availability and reuse eligibility are partly distributed across manifests, backend capability classes, `can_reuse_offline_window` methods, WPF handlers, and UI selection logic. Add one auditable capability declaration/check that describes full, on-demand, combined availability and each reuse direction; have existing shared orchestration consume or be validated against it. Algorithms still compute their own windows and declare only scientifically valid reuse. Reuse requires matching source, current algorithm identity, scientific config, preprocessing, and canonical sample bounds.

   Current code-based baseline (to be verified by tests during implementation): all nine algorithms have full dynamic and on-demand single-window implementations. The existing `OfflineWindowReuse` bridge can project full-result windows into preview for PSD, STFT, RBP, Band Ratio, Peak Frequency, IAPF, and Theta/Beta through `can_reuse_offline_window`; FAA and Brainbeat do not declare that hook. The inverse on-demand→full path is implemented in the shared backend publisher/consumer flow, with algorithm-specific window result validation. These are directional implementation facts, not proof that combined mode has equal real-recording validation/performance for every algorithm. The 2026-10-17 real-recording combined stress report specifically validates PSD; 2026-10-15 documents normal-path user acceptance for all nine without per-algorithm stress measurements.

   Alternative: implement full/on-demand logic separately in every algorithm runner or let the UI decide which cache to trust. Rejected because lifecycle and identity rules would drift; UI is not authoritative for scientific reuse. Forcing every algorithm to have identical reuse in both directions is also rejected because output shape, paired-channel semantics, and artifact provenance differ.

4. **Current version is explicit, never inferred.** Add a current/active marker or equivalent explicit declaration and require exactly one current scientific version per official algorithm ID. Resolver, API catalog and WPF selection consume that explicit choice. Advancing a version replaces the current executable implementation; no lexical sorting or compatibility adapter is added.

   Alternative: continue registering several executable versions and reject only ambiguous requests. Rejected because the user explicitly wants the latest version as the sole product target without patches or compatibility behavior for old versions. Old stored bytes are not intentionally deleted, but the current application may no longer expose them.

5. **Do not silently collapse scientific or storage semantics.** First inventory duplicate fields and their consumers. Keep distinct fields where meanings differ (scientific version, implementation identity, Definition version, dynamic result-contract version). Consolidate only fields with identical semantics. Avoid database-wide deletion or rewriting during this change.

   Alternative: use one generic `version` value everywhere. Rejected because it would conflate mathematical definition, implementation build, Definition provenance, and transport contract, damaging traceability.

6. **Migrate with a parity gate.** Add contract/completeness tests before replacing catalog construction. Compare all nine current catalog entries, request schemas, output units/shapes and module identities before/after. Build a per-algorithm matrix for full, on-demand, and combined strategies, including reuse eligibility and unsupported reasons. Run backend and WPF suites, API contract tests and strict OpenSpec validation. Any scientific output difference blocks this management change and is routed to a separate scientific-contract proposal.

7. **Treat onboarding and evidence as release inputs.** A new algorithm is not ready solely because it appears in the catalog or its module loads. Its onboarding record must link the authoritative declaration, backend and client registrations, required tests, per-strategy implementation/validation status, directional reuse evidence, and a reproducible validation artifact or an explicit unsupported/unvalidated reason. The completeness gate reports missing links by algorithm and layer. Combined mode requires combined-path evidence; separate full and on-demand success does not establish it.

8. **Trace results without promising legacy readability.** Persist current algorithm/scientific and implementation identities, normalized configuration, execution strategy, actual sample range, and producing/serving provenance on each new Run/result. Validation evidence records the tested identity and reproducible test/report reference. This preserves auditability of stored historical metadata when a version retires, while the latest-only policy still makes no promise that the current application can load or render its artifacts.

## Risks / Trade-offs

- [Risk] The existing official catalog contains scientific metadata not represented in manifests → inventory every consumer first; either add a typed module-owned declaration or leave a field in an explicitly separate Definition-provenance adapter until parity is demonstrated.
- [Risk] WPF can be missing an algorithm even when the backend is complete → cross-layer completeness tests must list required client integrations and report missing IDs by registry.
- [Risk] Collapsing distinct reuse directions may mix incompatible windows or claim nonexistent support → record and test full→preview projection separately from on-demand→full reuse, with algorithm-specific evidence for each.
- [Risk] Treating all algorithms as capable of combined execution may expose unsupported or expensive paths → declare support per algorithm and strategy, with explicit unavailable reasons and no assumption that all algorithms support every path.
- [Risk] Retired Runs and artifacts may become inaccessible in the latest client → no migration, adapter, or special rendering path is planned; this loss of access is an explicitly accepted trade-off. Stored bytes are not intentionally deleted by this change.
- [Risk] Version cleanup could alter cache identity or queued Run behavior → resolve and persist current identity once before queueing; reject retired identities before reading EEG or scheduling.
- [Risk] Metadata consolidation could accidentally alter scientific meaning → parity tests cover catalog schemas and real/synthetic numerical fixtures; any science delta stops the change.
- [Risk] Over-centralizing metadata could turn manifests into a second algorithm DSL → keep manifests declarative and bounded; formulas remain in typed module code, not a generic expression engine.
- [Risk] A declaration may be mistaken for proof or a checklist may become stale → the release gate compares declared capability, implementation hooks, tests, and evidence status, and marks missing proof as unvalidated rather than ready.

## Migration Plan

1. Inventory current metadata and every reader in backend catalog, API, Run resolver, WPF descriptor/handler registries and tests. Classify each field as scientific-module-owned, shared-runtime-owned, storage/provenance-owned, or client-presentation-owned.
2. Add explicit active-version resolution and tests without changing the currently active version for any algorithm.
3. Add completeness checks for all nine existing algorithms, record intentional client-specific registrations, and capture full/on-demand/combined support plus directional reuse capability from code and validation evidence.
4. Move only demonstrably duplicated catalog/Definition metadata behind module-owned declarations and generic composition; run catalog and request-schema parity tests after each group.
5. Align strategy capability declarations and UI/backend gating with the existing shared lifecycle/orchestration. Do not rewrite the scheduler or duplicate window computation. Validate declared strategies and each reuse direction, documenting unsupported or not-yet-validated combinations.
6. Remove retired calculator registrations and compatibility routes from the current runtime. Do not migrate or add special display support for retired Runs/artifacts; reject retired identities submitted for execution. Do not intentionally delete stored source/run/artifact bytes in this change.
7. Implement the onboarding checklist and machine-readable completeness output; verify a deliberately incomplete fixture is rejected with algorithm ID, layer, and missing field.
8. Verify traceability from declaration through Run/result provenance and validation evidence for each supported strategy, and prove retired identity metadata remains distinguishable without requiring legacy artifact rendering.
9. Run focused and full backend/WPF tests, strict OpenSpec validation, and `git diff --check`; publish the algorithm onboarding and ownership documentation with links to declarations, registrations, tests, and evidence.

Rollback restores the prior registration/composition code and active-version mapping. Any retired module removed from the build must be restored by reverting that code change. No database migration or intentional deletion of stored data is planned; access to legacy data during the latest-only state is not guaranteed.

## Open Questions

None. The user explicitly chose latest-only behavior without historical-version compatibility; old stored bytes may remain physically present but need not be accessible through the current application.
