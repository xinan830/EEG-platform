# Unify Algorithm Detail Presentation

## Why

Algorithm detail pages already use shared execution, quality, provenance and
dynamic result views, but their outer frame and heading presentation are repeated
in four XAML files. A correction to shared spacing or typography should not require
editing every card, nor change scientific bindings.

## What Changes

- Add named card-frame and heading styles to the existing AnalysisStyles dictionary.
- Migrate the four existing shared views to these resources; retain their view-model contracts.
- Keep independent review and detail playback semantics, algorithm parameters and charts unchanged.
- Test independent view loading and local theme override propagation.

## Impact

WPF presentation only. No API, backend, units, scientific parameters, timing or Run
lifecycle change. No new algorithms are added to offline review. Rollback consists
of restoring these four frame/title declarations; immutable recordings and Runs are untouched.
