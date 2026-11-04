## Why

Review charts show values from backend-selected dynamic windows, but the actual time span is not consistently visible beside each visualization. Showing the returned range at the lower-left makes it clear which recording interval the plotted result represents.

## What Changes

- Show the backend-returned current analysis range on the PSD spectrum and PSD topomap.
- Show the backend-returned current analysis range on each RBP history chart and the RBP topomap view.
- Keep the label blank or explicitly unavailable when the backend range is missing; do not infer it from the cursor or displayed history.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/on-demand-dynamic-review`: Require visible actual analysis ranges on PSD/RBP Review visualizations.

## Impact

WPF Review result view models and PSD/RBP panel XAML are affected. Backend contracts, algorithm calculations, channel ordering, units, quality states, and historical results do not change. This is display-only and requires no data migration.
