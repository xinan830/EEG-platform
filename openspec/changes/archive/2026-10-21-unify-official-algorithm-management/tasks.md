## 1. Management Contract Inventory

- [x] 1.1 Map identity, version, parameters, capabilities, output schema, provenance, and presentation metadata to their current owners and readers across backend and WPF.
- [x] 1.2 Define the authoritative typed official-module declaration and classify Definition persistence fields separately from scientific metadata.
- [x] 1.3 Add a current-version field/resolution contract and tests proving exactly one explicitly current version per official algorithm ID, with no sorting-based selection.
- [x] 1.4 Build an evidence-based per-algorithm matrix for full, on-demand, combined preview-plus-background-full, full→preview projection, and on-demand→full reuse; distinguish implemented, validated, unsupported, and unknown rather than inferring from dynamic mode.

## 2. Completeness Gate

- [x] 2.1 Add backend tests that every built-in official module has unique identity, valid config/parameter schema, supported modes, output units/shapes, and registered runner.
- [x] 2.2 Add cross-layer completeness tests for the nine backend catalog IDs, WPF UI descriptors, result handlers, detail modules, and declared output types.
- [x] 2.3 Add a regression test proving a missing registration is reported with its layer and algorithm ID and cannot be treated as release-ready.
- [x] 2.4 Extend the completeness gate to check explicit per-algorithm strategy availability and directional reuse declarations against backend capabilities, WPF availability, and validation evidence.
- [x] 2.5 Make the completeness gate distinguish `implemented`, `validated`, `unsupported`, and `unvalidated` for every strategy and reuse direction; block release-ready status when required support is declared without matching implementation and evidence.
- [x] 2.6 Prove the gate reports algorithm ID, integration layer, and exact missing declaration/registration/test/evidence item using an intentionally incomplete fixture.

## 3. Metadata Ownership Consolidation

- [x] 3.1 Refactor public catalog assembly to derive official entries from the authoritative module declarations while preserving current catalog response parity.
- [x] 3.2 Replace avoidable per-algorithm Definition metadata branches with typed module declarations and generic Definition composition; retain only storage provenance that cannot be safely derived elsewhere.
- [x] 3.3 Keep WPF registries process-local but make their required entries verifiably match backend identity, capabilities, parameters, and output contracts.
- [x] 3.4 Preserve each algorithm's own scientific config, formulas, channel roles, quality extensions, and evidence; verify no science code moved into generic services or WPF.
- [x] 3.5 Align capability metadata and backend/WPF strategy gating with the existing full/on-demand/combined orchestration; do not rebuild shared scheduling, persistence, or window identity infrastructure without an evidenced gap.
- [x] 3.6 Encode full→preview and on-demand→full reuse as separate capability paths; validate exact source, identity, configuration, preprocessing, sample range, result shape, and algorithm-specific evidence before reuse.
- [x] 3.7 Publish an actionable new-algorithm onboarding checklist naming authoritative declaration and registration locations, required backend/WPF integration, tests, validation artifacts, and release gate; link each checklist item to its automated check where possible.

## 4. Latest-Only Version Policy

- [x] 4.1 Update catalog and Run resolution to expose/execute only the explicitly current scientific version and reject retired versions before reading EEG or queueing.
- [x] 4.2 Remove current-runtime compatibility calculators/adapters for retired official versions without deleting stored recordings, Runs, or artifacts.
- [x] 4.3 Add tests for queued identity immutability, current version change, rejection of retired execution requests, and absence of implicit lexical/numeric latest selection; do not add legacy Run/artifact display compatibility tests.
- [x] 4.4 Add strategy tests per declared capability: full/on-demand values, states, and provenance match for identical windows; combined mode returns preview and background status correctly; each directional reuse path is tested only for algorithms declaring and validating it, and incompatible windows are never reused.
- [x] 4.5 Require combined-strategy-specific evidence before marking combined mode validated; verify independent full and on-demand success alone leaves combined mode unvalidated.
- [x] 4.6 Verify traceability from current module/scientific/implementation identity through frozen Run configuration, selected strategy, actual sample bounds, producing/serving provenance, tests, and validation report/artifact identity.
- [x] 4.7 Verify retired stored identity metadata remains identifiable for audit while adding no compatibility execution, migration, interpretation, or display behavior for retired artifacts.

## 5. Verification And Documentation

- [x] 5.1 Verify catalog, request parameter schema, scientific/implementation identities, output units/shapes, and current numerical results remain unchanged for all nine algorithms and every strategy/reuse path declared supported.
- [x] 5.2 Run relevant backend and WPF tests, full suites, OpenSpec strict validation, and `git diff --check`.
- [x] 5.3 Document field ownership, evidence-based per-algorithm strategy/directional-reuse matrix with separate implementation and validation states, and the actionable onboarding checklist, including the explicit latest-only policy and lack of retired-version compatibility.
