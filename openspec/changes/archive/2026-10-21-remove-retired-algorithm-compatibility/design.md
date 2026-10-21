## Context

See proposal.md for motivation. The completed `unify-official-algorithm-management` change has 27/27 tasks and was pushed as d1c09a3; its spec deltas have not yet been archived into main specs. Implementing this cleanup requires reconciling that baseline first so the old main-spec compatibility-import requirement does not remain authoritative by accident.

Confirmed consumers:

| Candidate | Evidence | Decision |
| --- | --- | --- |
| `eeg_core/official_algorithms/validation/legacy_reference.py` and wrappers | Imported by historical shadow tests; imports production calculators | Remove historical suite; keep current independent reference suite |
| `eeg_core/realtime_spectral.py` | Historical helper tests and shadow references | Delete after consumer closure |
| `legacy/definition_engine.py` | `services/definitions.py` imports graph and parameter validation | Extract current validation responsibility; delete unused execution/formula entry points |
| `FullRunWindowPreviewAdapter.WindowProvenance` | Adds missing STFT scope and inner-axis values with `legacy_full_artifact` tags | Reject missing required evidence; never manufacture it |
| Historical FAA discard/cap constants | No production consumer; only a characterization test imports the alias | Delete unused constants; current paired-epoch calculations remain unchanged |
| `theta_beta/official.py` bundled metrics | Only historical numerical tests consume it; current runner uses `compute_theta_beta` | Delete bundled calculator and its reference; compare current single-channel implementation against independent inputs |
| Catalog contracts under `eeg_core/official_algorithms` | Only catalog DTO is active; manifest alias has no consumer | Move DTO to `algorithms/catalog_contracts.py`, delete alias and old package |

## Goals / Non-Goals

**Goals:** Close the dependencies of each removed entry point, make current validation ownership explicit and make unsupported artifacts observably unavailable.

**Non-Goals:** Do not change scientific versions, formulas, preprocessing, window alignment, unit conversions or declared strategy capabilities. Do not delete all occurrences of words such as fallback, compatibility or legacy; hardware compatibility, rendering resilience and storage migrations have different responsibilities. Do not introduce old-to-new artifact conversion.

## Decisions

1. Use dependency-based deletion. Inspect imports, re-exports, authority manifests, CLI tools and tests before removal. Preserve the current independent reference tests and NPZ/JSON evidence. Deleting an entire legacy-named directory without checking consumers would remove current validation.
2. Put current Definition graph/parameter validation in a dedicated current domain module. Retain required node metadata and errors; remove execution-only AST/formula/math imports after proving they have no current consumer. No forwarding shim remains under `app/legacy`.
3. Validate provenance in both WPF Full Run preview paths. The indexed-window path currently constructs a temporary object containing only state, so it also needs real backend provenance to reach the adapter; rejecting only at the adapter without fixing this data flow would break current combined operation. Reuse the existing typed backend window response and result failure presentation where possible. Add a stable missing-contract reason, and avoid throwing an unhandled UI exception.
4. Delete the old realtime metric document from the current index and update surrounding current explanations. Keep dated audit/decision reports as historical records; clarify that they are not maintained execution guidance. Update scientific authority ownership and architecture tests alongside deletions.
5. Keep database structural migrations and immutable storage. Rollback restores code, not old scientific execution compatibility or rewritten artifacts.

## Risks / Trade-offs

- Hidden consumer outside a direct import -> inspect command-line tools, manifest strings and re-exports, then run full backend tests.
- Removing a mixed historical test loses current coverage -> preserve its current assertions in the appropriate current test module before deleting historical assertions.
- Current indexed Full Run response omits evidence -> verify backend serialization and both WPF preview paths with valid and missing-contract cases.
- Old results become unavailable -> report a stable reason and recompute action; preserve underlying bytes and identity.
- Source files exceed project limits -> record responsibility assessment in `docs/code-size-policy.json` for touched files above 400 lines; follow stricter 600/1000-line rules.

## Migration Plan

1. Validate and archive the completed management baseline with its concise validation report before applying this cleanup; reconcile affected current specs. The new cleanup is scaffolded separately for review.
2. Record a consumer inventory, current reference identities and artifact checksums. Add missing-contract/current-contract regression cases first.
3. Remove retired references and consumers; extract current Definition validation; correct Full Run provenance transport and rejection behavior.
4. Update current documentation and manifests; run backend, WPF, OpenSpec strict, file-size checks and diff checks. Recheck the current nine-algorithm numerical and strategy baselines without regenerating expected results to hide drift.
5. Write a concise validation report, update tasks, then commit source/tests/docs and controlled validation artifacts only. Database and recording storage remain untouched. Restore the code commit for rollback if needed.
