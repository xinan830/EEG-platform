# Add Official Brainbeat Run

## Why

Brainbeat currently exists only as a realtime shadow-validation descriptor.
The platform needs a traceable offline/static and windowed/dynamic AnalysisRun
for the WPF workbench.

## Scope

- Promote Brainbeat to a runnable official backend module.
- Define Brainbeat as `relative_theta(Fz) / relative_alpha(Pz)` using the
  IAPF-derived bands for each independent analysis window.
- Add explicit Fz and Pz source-channel inputs and structured failure evidence.
- Add a dedicated WPF detail module and result handler.

## Non-goals

- Do not use the historical realtime EMA implementation in AnalysisRun.
- Do not add live acquisition or playback integration.
- Do not remove the historical shadow implementation in this change.
