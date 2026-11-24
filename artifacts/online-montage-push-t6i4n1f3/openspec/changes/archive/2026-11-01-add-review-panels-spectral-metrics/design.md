## Context

See `proposal.md` for the motivation. Backend catalog entries already declare dynamic scalar outputs and the on-demand registry resolves their current capabilities. Review already has a formula-free dynamic-window adapter and one-active-panel cursor/lifecycle coordination, but its parameter setup and presentation dispatch are specific to RBP/STFT.

FAA consumes a distinct F3/F4 pair and declares configured continuous preprocessing. IAPF and Theta/Beta each consume one channel. Brainbeat consumes a distinct Fz/Pz pair and declares configured continuous preprocessing. These scientific contracts remain unchanged.

## Goals / Non-Goals

**Goals:**

- Make Review algorithm composition extensible through explicit per-algorithm registrations for configuration and result presentation.
- Add four scalar trend panels while retaining the existing RBP and STFT Review behavior and the shared recording/time/cancellation lifecycle.
- Keep backend policy, role mapping, identity, units, quality, and provenance authoritative.

**Non-Goals:**

- Change formulas, preprocessing scope, window policy, thresholds, versions, cache identity, or full-run detail pages.
- Run multiple Review panels concurrently or make waveform playback wait for algorithm results.
- Add backend endpoints or transform the four algorithms into one composite algorithm.

## Decisions

1. **Use a Review registration boundary for all panels.** A registration identifies the catalog algorithm and owns parameter initialization/config serialization plus the matching presenter state. The generic Review coordinator continues to own one active subscription, cursor target, cancellation, and stale-response fencing. This removes repeated algorithm-specific branches from request preparation and result binding. An algorithm-ID conditional scattered through the main Review view model was rejected because it would expand for every panel.

2. **Keep parameter and result contracts with their algorithm modules.** FAA and Brainbeat registrations construct explicit ordered channel pairs; IAPF and Theta/Beta construct a single selected channel. All read the current catalog version and declared dynamic policy. Generic chart code receives a validated scalar point plus its unit and actual range; it does not know formulas or compute values. A full catalog workspace embedded in Review was rejected because it would duplicate recording and cursor ownership.

3. **Reuse the generic dynamic-window adapter and a scalar chart surface.** The adapter already validates recording/algorithm identity and translates the current backend response into the Review window contract. A small common scalar history/chart presents values by their declared output field and unit; algorithm-specific presenters validate field names and any required provenance before publishing. Copying four dynamic-window clients or implementing scientific parsing in the Review shell was rejected.

4. **Preserve current science and errors exactly.** FAA and Brainbeat retain their paired-source and declared continuous preprocessing contracts. Missing role mappings, quality rejection, unavailable input, invalid units, and malformed results remain unavailable with reasons; there is no channel substitution, zero fill, previous-value fallback, or numerical migration.

5. **Register all six Review panels in the existing one-active-panel composition.** RBP and STFT migrate to the same lifecycle boundary; their existing specialized charts remain specialized. The four scalar algorithms share the scalar chart presentation. A panel switch clears the old presenter before the new subscription attaches.

## Risks / Trade-offs

- [Input identity could be confused across montage aliases] -> Resolve exact selected output IDs from the active montage and serialize only role-matching outputs in declared order; test initial selection and montage changes.
- [A generic scalar chart could hide algorithm contract mistakes] -> Keep per-algorithm expected output field, unit, and provenance validation in module-owned presenters and test rejected/malformed responses.
- [FAA and Brainbeat have whole-recording preprocessing semantics] -> Preserve the current declared preprocessing scope; this feature does not claim first-window latency is bounded by window size or alter scientific numbers.
- [Review's registration could become a second algorithm catalog] -> Compose registrations once from the existing closed desktop algorithm list and require every registration's ID to match the backend catalog entry; do not duplicate formula/version metadata.

## Migration Plan

No database, API, cache, or stored-result migration is required. Replace only internal Review dispatch registrations and update callers/tests with them. Rollback removes the four menu/panel registrations and restores the previous Review composition; persisted backend results and recordings remain unchanged.
