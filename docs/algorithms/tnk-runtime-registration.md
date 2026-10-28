# TNK runtime registration

The single backend composition boundary is `backend/app/algorithms/builtin_modules.py`.
It exposes one selectable `tnk_biofeedback` group through
`build_tnk_runtime_adapters()`. The group composes 16 independent internal
`tnk_*` scientific modules; child IDs are used for dispatch, evidence, and
provenance and are not public selectable catalog entries.

The adapter supplies platform plumbing: catalog manifest construction,
role-to-channel validation, normalized PSD loading for spectral children, and
typed raw time-series loading for the four ANT-derived Fz children. Formula,
quality, state, and evidence ownership remains in `backend/app/algorithms/tnk/`.

The raw-Fz children are `tnk_crimson_meditation`,
`tnk_zenlite_calmness`, `tnk_zenlite_awareness`, and
`tnk_zenlite_spectral_relaxation`. Their source contract is V/float64 with
explicit Fz role and recording/montage identity. They own 250/256 Hz
resampling and fixed legacy preprocessing; the group never substitutes a PSD
window for that input. TFLite assets are hash- and shape-checked, and an
optional-runtime/model failure is a structured unavailable child result.

The group is registered and runnable for dynamic runs only. A new static
request is rejected before scientific execution with the runtime's structured
unsupported-mode response; it is not converted into a dynamic request. Child
modules retain their own calculation, quality, and provenance boundaries. FAA
produces an explicit unavailable child result in arbitrary cursor windows
because its contract requires a complete paired segment/report. Group-level
numerical parity, real-recording validation, and manual WPF acceptance remain
separate release gates.
