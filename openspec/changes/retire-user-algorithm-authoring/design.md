# Design

The official catalog is the only selectable algorithm catalog. Its entries
continue to derive runnable identity and parameters from Runtime manifests.
The browser no longer renders graph-authoring, publishing, clone, deletion or
scalar preview controls, nor submits `definition_metric` Runs.

The existing Definition repository and official installer remain internal.
`GET /api/algorithm-definitions`, its item/version reads and version comparison
remain available for historical inspection. Prior mutation/preview routes
return `410 USER_ALGORITHM_AUTHORING_RETIRED` without writing data or running
the scalar graph. The retired `definition_metric` Run request continues to
return its existing stable `410` code. Historical Runs and artifacts continue
to deserialize and export from stored evidence.

`POST /api/recordings/{id}/analysis` has no active caller and is removed. The
historical `GET /api/analyses/{id}` remains read-only. The `detail` error field
and dynamic `warmup` view are kept where existing readers still require them;
this change does not alter persisted response shapes.

No source recording, Run, artifact or Definition row is rewritten or deleted.
