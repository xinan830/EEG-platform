# Design

The researcher view reads the latest persisted definition version and renders
the curated feature metadata already saved by the metric builder. It presents
Input A, approved binary operator, Input B, named output, and unit. It remains
a presentation layer: all metric execution and all EEG-derived values continue
to come from the backend.

Private definitions have a visible Delete control with a confirmation. The API
is authoritative: it deletes the definition and its versions only if neither
an AnalysisRun nor a BatchRun references that definition. Platform-official
definitions are installed by backend code and are never creatable or deletable
through the browser API. A blocked deletion leaves all records intact and
returns a structured error.
