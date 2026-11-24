# Improve core research workbench UX

## Why

The local workbench currently exposes developer-oriented controls alongside the
ordinary EEG review flow. Users must infer the relationship between waveform
review, spectral analysis, time-frequency analysis, results, and engineering
inspection from a single dense page.

## What changes

- Add task-oriented navigation for waveform review, spectrum, spectrogram, and results.
- Make the ordinary view the default and move runtime/debug inspection behind
  an explicit developer-mode control.
- Use Chinese-first labels that distinguish viewer-only display controls from
  offline analysis settings.
- Preserve every current analysis API and all EEG mathematics.

## Non-goals

- No new EEG algorithms, formula builder, routing framework, authentication,
  project workflow, batch workflow, or backend capability is introduced.
