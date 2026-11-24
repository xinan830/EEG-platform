# Design

`RunCreateRequest` accepts only `spectrum`, `spectrogram`, and
`official_algorithm`. The old `/api/analyses` route and
`/api/algorithm-definitions` route are removed; the official catalog remains
available through `/api/algorithms` and installs its immutable internal
Definition records through the composition root.

Migration 010 deletes `definition_metric` and `definition_preview` Runs,
their artifacts, local-user Definitions and versions, and the obsolete
`analyses` table. The `recordings` table/files and official Definition rows are
untouched.

The migration is intentionally destructive for retired test data. A backup is
required before applying it to a non-disposable environment.
