# Change 05 Algorithm Form Builder Report

## Conclusion

`add-algorithm-form-builder` adds a traceable local definition workbench while
preserving the existing EEG Viewer and analysis APIs. The feature is an
authoring and scalar-simulation surface, not a replacement EEG processing
pipeline and not a clinical decision system.

## Delivered Contract

- One canonical `DefinitionVersionDraft` synchronizes the form and advanced
  JSON view. Invalid or incomplete JSON keeps the prior valid draft unchanged.
- Definition switching is race-protected: a stale version response cannot
  overwrite the state selected by a newer request.
- The API exposes a closed authoring vocabulary through
  `GET /api/algorithm-definitions/capabilities`.
- Definition validation, persistence, publication, cloning, comparison, units,
  graph semantics, and scientific output remain backend responsibilities.
- `POST /api/algorithm-definitions/preview-run` creates a persisted,
  `is_preview=true` `AnalysisRun` with source identity, absolute requested and
  actual ranges, configuration/draft summaries, build environment, units, and
  quality state.
- Preview accepts typed scalar simulation inputs only. It does not read EEG
  samples, write a formal artifact, or overwrite any formal run.
- Missing or unavailable preview output is a structured gate failure with
  `null`; no zero-valued scientific result is invented.
- Official composite definitions are visible but generic execution is disabled
  where that engine cannot faithfully implement their semantics.

## Test Evidence

Executed on 2026-09-14 from the local working tree:

| Check | Result |
| --- | --- |
| `openspec validate add-algorithm-form-builder --strict --no-interactive` | PASS |
| `backend/.venv/Scripts/python.exe -m pytest -q` | PASS, 144 tests |
| `frontend: npx vue-tsc --noEmit` | PASS |
| `frontend: npm test -- --run` | PASS |
| `frontend: npm run build` | PASS |
| `git diff --check` | PASS before archive/commit |

The backend suite covers preview provenance, invalid units, unavailable output,
and isolation from formal runs. Frontend tests cover absolute preview request
parameters, structured API errors, form/JSON draft retention, and stale-request
protection. Existing legacy APIs remain covered by the backend regression suite.

## Tooling Limitation

The automated local browser smoke test could not start because the Codex
browser bridge reported that its local authentication token was unavailable.
This is a local tooling limitation, not an application test failure. Static
type checking, Vitest, and the production bundle all passed; manual browser
smoke verification remains advisable when the local bridge is available.

## Remaining Boundaries

This change does not add a visual DAG editor, raw-EEG-to-primitive execution
adapter, batch jobs, arbitrary Python, multi-user permissions, or clinical
interpretation. Those items remain explicitly scoped to later changes.
