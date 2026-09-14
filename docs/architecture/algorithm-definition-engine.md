# Algorithm Definition Engine

Definitions are backend-only, versioned research graphs. A Definition identity
is mutable only in its descriptive metadata; each stored version is a distinct
SemVer snapshot. Publishing changes a draft version's lifecycle state but never
rewrites graph, schema, units, quality rules or digest. Corrections require a
new SemVer version.

The graph can reference only the closed primitive registry. It is topologically
sorted before execution and rejects cycles, unknown nodes, missing inputs and
unit incompatibility with stable codes. Formula parsing uses an AST allowlist
for names, finite numeric constants, arithmetic and `ln`; it does not call
`eval`, import modules, inspect attributes or accept indexing/calls.

`POST /api/algorithm-definitions/preview` is an in-memory scalar preview. Its
response is explicitly `preview: true` and `persisted: false`; it cannot
overwrite a formal AnalysisRun. Current spectrum, spectrogram, Viewer and
official algorithms remain on their existing paths until Change 04 completes
shadow validation.
