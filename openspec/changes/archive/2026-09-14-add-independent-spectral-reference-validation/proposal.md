## Why

The repository has a standalone SciPy/MNE command-line script and unit-level
independent preprocessing coverage, but no reproducible link from a recording
to a persisted, exportable, pointwise independent PSD comparison.  A result
workbench user therefore cannot determine whether a frozen
`offline-spectral-v3` result agrees with a separately implemented reference.

## What changes

- Add a read-only spectral reference validation operation for a recording,
  selected range, and requested channel order.
- Calculate the reference with NumPy/SciPy only; it must not call production
  preprocessing, Welch, or band-integration helpers.
- Compare reference PSD values to the existing production spectrum response
  and persist finite pointwise evidence, declared tolerances, configuration
  digest, source identity, and environment in `ValidationRun`.
- Make the persisted evidence available in the existing validation report and
  therefore in an existing result export package.

## Non-goals

- This does not change `offline-spectral-v3`, the Viewer pipeline, spectral
  API contracts, quality thresholds, or clinical interpretation.
- This does not persist raw EEG samples, original filenames, or participant
  identity information.
- This does not claim that numerical parity establishes clinical validity.
