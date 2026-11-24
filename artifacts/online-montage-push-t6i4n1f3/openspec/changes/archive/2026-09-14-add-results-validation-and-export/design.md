## Design

`ResultService` reads immutable AnalysisRuns and verified NPZ artifacts through
the existing ArtifactStore. A result view contains metadata plus safe summaries;
arrays are not embedded in default JSON. Export creates a confined zip package
with `manifest.json`, `result.json`, optional `summary.csv`, verified `.npz`,
and requested validation report JSON.

The manifest includes schema version, run identity, algorithm/implementation
versions, configuration and source digests, actual range, units, quality,
artifact hashes, and export timestamp. It excludes original filename, source
samples, PII, and clinical claims.

Validation APIs retain independent expected/actual arrays and engineering
tolerances. UI labels them as measured data, algorithm output, or engineering
validation; none states a diagnosis.
