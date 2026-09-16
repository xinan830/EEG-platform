## 1. Baseline and contracts

- [ ] 1.1 Capture existing frozen spectral, IAPF, and historical Theta/Beta fixtures before moving executable code.
- [x] 1.2 Complete the import/call-graph audit for legacy algorithm entries and recording mapping consumers.
- [x] 1.3 Add runtime contracts, structured errors, parameter/result schemas, and registry boundary tests.

## 2. Runtime and algorithm modules

- [x] 2.1 Create the generic algorithm runtime and registry with no concrete algorithm-ID branches.
- [x] 2.2 Move IAPF into a canonical runtime algorithm module and prove frozen output equivalence.
- [x] 2.3 Implement canonical single-channel `official-theta-beta-v2` static and dynamic execution with null failure semantics.
- [ ] 2.4 Wrap user Definition graph execution in the same runtime result and catalog contracts.

## 3. Run and catalog cutover

- [ ] 3.1 Route new Run creation, validation, cache identity, provenance, and artifact serialization through the runtime.
- [x] 3.2 Add the unified `/api/algorithms` catalog and retire old official execution catalog usage.
- [ ] 3.3 Verify historical Runs and Artifacts remain read-only and exportable without old calculator imports.

## 4. Remove mapping and legacy execution paths

- [ ] 4.1 Remove active ChannelMapping model, import auto-mapping, recording mapping API/UI, and global role requirement.
- [ ] 4.2 Apply and verify idempotent SQLite migration that removes active mapping storage while retaining historical Run snapshots.
- [ ] 4.3 Delete audited compatibility facades, legacy executable algorithm paths, and generic algorithm-ID branches; add architecture regression tests.

## 5. Schema-driven frontend

- [ ] 5.1 Replace fixed official-ID request and unit logic with unified catalog and schema-driven parameter cards.
- [ ] 5.2 Render independent static/dynamic configuration per selected algorithm and keep display-only settings local.
- [ ] 5.3 Show raw selected channels and backend-returned values/reasons without frontend scientific calculation.

## 6. Verification and archive

- [ ] 6.1 Run targeted migration, runtime, IAPF, Theta/Beta v2, user-definition, catalog, and frontend interaction tests.
- [ ] 6.2 Run full backend pytest, frontend Vitest, type check, production build, OpenSpec strict validation, and diff checks.
- [ ] 6.3 Generate a validation report, update architecture documentation, archive the OpenSpec change, and validate all specs.
