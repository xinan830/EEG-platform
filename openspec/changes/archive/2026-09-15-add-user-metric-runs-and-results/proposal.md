# Run saved user metrics and present their results

## Why

The user metric builder can safely save an immutable formula such as Theta
power divided by Beta power, but it deliberately does not claim to have
measured a value from EEG. Researchers now need the next link: choose a
recording, channel, and time range; run the saved formula in the backend; and
inspect the resulting value with its unit, quality state, and provenance.

## What Changes

- Add a `definition_metric` AnalysisRun that resolves curated spectral inputs
  from a frozen offline-spectral-v3 static PSD result and executes a saved
  definition version in the backend.
- Add a separate waveform-and-algorithms workspace. It is the ordinary user
  surface that runs saved algorithms and presents their measured results; the
  algorithm library remains definition-oriented.
- Support both a single static result and a dynamic 10-second trailing-window
  series at one-second steps over a committed outer range.
- Present a scalar as a value card, not a fabricated one-point chart; provide
  a same-unit input comparison only when the underlying inputs are compatible.
- Keep future multi-window trend charts separate: their X axis is real time in
  seconds and their Y axis is the output's persisted unit, not arbitrary text
  selected by the browser.

## Non-goals

- Batch visualizations, arbitrary axis/unit overrides, official composite
  algorithm execution, and clinical interpretation.
- Browser-side PSD, band-power, or formula computation.
