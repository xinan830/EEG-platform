# User-friendly algorithm workbench

## Why

The algorithm-definition workbench currently exposes graph adapter fields such as
`official_result`, `ratio`, and `out` to every user. Those fields are required
for engineering provenance but are not a readable explanation of an EEG metric.
Users need a Chinese-first description of what an algorithm measures, how the
backend obtains it, and how to interpret its unit.

## What changes

- Make a readable Chinese algorithm explanation the default workbench view.
- Add an explicit `开发者详情` switch that exposes the existing form, graph,
  units, JSON, preview, publishing, and version-comparison controls.
- Preserve official definition identity, immutable versions, execution behavior,
  and editing restrictions.
- Reset stale action and preview messages when the selected definition changes.

## Non-goals

- This change does not alter EEG, PSD, IAPF, band-power, or quality-gate math.
- It does not make a clinical conclusion, nor does it compute scientific values
  in the frontend.
