## Why

The backend already has a common runtime contract, but official algorithm metadata and desktop registrations still have multiple maintenance points. As the catalog grows, those copies can drift or a new algorithm can be registered in one layer and omitted from another. The project needs one consistent management lifecycle while preserving a separate scientific implementation for every algorithm.

## What Changes

- **BREAKING** Make one explicitly active/current scientific definition the only selectable and executable version for each official algorithm ID; do not add compatibility adapters or keep retired calculators executable when an algorithm advances.
- Establish authoritative ownership for algorithm identity, user-facing metadata, parameters, capabilities, output declarations, and implementation identity; remove avoidable duplicate metadata and ID-specific catalog construction branches.
- Standardize backend and WPF module registration/composition checks so incomplete cross-layer integration is detected before release.
- Manage the three dynamic execution strategies—full analysis, on-demand analysis, and on-demand preview plus background full analysis—through one explicit per-algorithm capability contract. Keep the existing shared orchestration/runtime; make strategy availability and each reuse direction (full result to preview, or on-demand result to full Run) explicit and evidence-backed. Do not assume every algorithm has identical reuse support.
- Seed the capability matrix from current implementation and evidence: all nine have full dynamic and on-demand entry points; full-result-to-preview reuse is implemented for PSD, STFT, RBP, Band Ratio, Peak Frequency, IAPF, and Theta/Beta; FAA and Brainbeat do not declare it. Do not equate this code-level fact with equivalent real-recording combined-strategy validation for every algorithm.
- Define a repeatable new-algorithm checklist and automated completeness tests covering catalog exposure, backend module, WPF result handling, parameters, output shape/units, and validation entry points.
- Make the onboarding path operational and release-blocking: identify the required declaration and registration locations, required tests and evidence artifacts, and ensure completeness output names the algorithm and each missing item.
- Distinguish implemented capability from validated capability in declarations and release checks. In particular, a combined strategy is not considered validated merely because its full and on-demand paths work independently.
- Preserve an auditable chain from a current algorithm declaration and implementation identity to Run configuration, selected strategy, result provenance, relevant tests, and validation evidence, including for retired versions whose stored results are no longer guaranteed to be readable.
- Keep each algorithm's scientific formulas, specialized inputs, and algorithm-specific evidence inside its own module. Shared runtime code handles lifecycle, validation, scheduling, provenance, persistence, and transport only.
- Do not migrate or maintain compatibility for retired-version Runs/artifacts; the current application targets the latest declared algorithm versions only. This change does not intentionally delete stored bytes, but old Runs and artifacts may no longer be readable or displayable.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `analysis/official-algorithm-catalog`: define the active/current official algorithm entry as the sole selectable version and expose complete management metadata from its authoritative module declaration.
- `analysis/algorithm-runtime`: require cross-layer registration completeness, explicit current-version selection, and current-version-only execution without legacy calculator adapters.
- `analysis/official-algorithm-foundation`: require a consistent module management contract while preserving algorithm-owned scientific computation and extensions.

## Impact

- Affects backend module manifests/catalog assembly, closed built-in registration, runtime resolution and provenance tests.
- Affects WPF algorithm UI/result-handler composition and completeness checks; public HTTP result shapes should remain stable unless a reviewed contract change proves otherwise.
- **BREAKING:** Runs and artifacts created by retired algorithm versions have no migration, execution, interpretation, or display compatibility guarantee and may become inaccessible in the current application. This plan does not intentionally delete stored bytes or recordings.
- No formula, filter, unit, channel-role, quality-gate, or scientific-definition change is intended. If metadata consolidation reveals a scientific disagreement, stop and propose a separate scientific-contract decision instead of choosing a formula during this management change.
- No new dependency or database-wide data deletion is intended.
