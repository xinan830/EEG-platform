# TNK runtime registration

The single backend composition boundary is `backend/app/algorithms/builtin_modules.py`.
It exposes one selectable `tnk_biofeedback` group through
`build_tnk_runtime_adapters()`. The group composes 12 independent internal
`tnk_*` scientific modules; child IDs are used for dispatch, evidence, and
provenance and are not public selectable catalog entries.

The adapter only supplies platform plumbing: catalog manifest construction,
role-to-channel validation, and normalized PSD window loading. Formula,
quality, state, and evidence ownership remains in `backend/app/algorithms/tnk/`.

The group is available for static and dynamic runs. Child modules retain their
own calculation, quality, and provenance boundaries. FAA produces an explicit
unavailable child result in arbitrary cursor windows because its contract
requires a complete paired segment/report. Group-level on-demand/combined
capabilities remain unvalidated and must not be presented as accepted. WPF
manual acceptance remains the final release gate.
