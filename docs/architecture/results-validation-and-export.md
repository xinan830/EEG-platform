# Results, Validation, and Export

The result drawer is read-only: it lists backend AnalysisRuns for the selected
Recording and renders returned status, versions, artifacts, and quality/error
state. It does not calculate EEG values or infer clinical meaning.

`GET /api/runs/{id}/result` returns a structured result view. `GET
/api/runs/{id}/export` creates a ZIP containing `manifest.json`, `result.json`,
`summary.csv`, verified NPZ artifacts, and optionally a persisted engineering
validation report. The manifest excludes original filenames, source samples,
participant identity, and clinical conclusions. Corrupt artifacts fail export.

ValidationRun reports remain engineering evidence under explicit tolerances.
PASS means numerical agreement under that stated contract, not clinical
validity or a diagnosis.
