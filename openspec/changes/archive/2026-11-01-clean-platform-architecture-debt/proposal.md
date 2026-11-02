## Why

The repository has accumulated architectural debt after several completed
feature migrations. Internal compatibility paths, oversized composition files,
direct View-to-`MainWindow` navigation, and isolated build output make it hard
to tell which path is current and increase the risk of changing shared state
or scientific behavior accidentally. The active-change queue is now empty, so
this cleanup can be planned as one controlled structural change.

## What Changes

- Inventory every suspected compatibility path and classify it as historical
  file support, current scientific implementation, active adapter, or obsolete
  internal fallback.
- Remove only confirmed obsolete internal fallbacks and transitional WPF
  compatibility types after all callers, XAML `x:Class` values, generated BAML
  references, and composition sites have moved together.
- Preserve historical EEG file formats, raw recordings, persisted results,
  current TNK scientific children (including Crimson and Zenlite), and current
  public API/result identities.
- Reduce `App`/`MainWindow` to composition-root and shell responsibilities;
  move feature-owned construction and lifecycle details to explicit module
  composition without adding a service locator or dependency-injection
  framework.
- Replace feature Views' direct `MainWindow` casts with the narrowest existing
  command or navigation boundary; retain `Window.GetWindow` only when it is
  needed for modal-dialog ownership.
- Route isolated build and test output through the existing ignored temporary
  root, add ignore rules only for verified generated paths, and remove only
  unused generated output.
- Record a validation report for each slice and keep this change structural;
  no scientific formula, time, unit, channel, storage, or public API contract
  is intentionally changed.

## Capabilities

### New Capabilities

None. This is a structural refactor and repository-hygiene change.

### Modified Capabilities

None. User-visible and scientific behavior remain governed by the current main
specifications; the change declares `skip_specs: true` because it does not
introduce or alter a behavior contract.

## Impact

- WPF desktop source layout, `App`/`MainWindow` composition, feature Views and
  navigation commands.
- Internal Python/WPF algorithm adapter construction only where a confirmed
  obsolete fallback is removed.
- Git ignore rules and temporary build/test output locations.
- No database migration, recording rewrite, historical-file format removal,
  API version change, result conversion, or scientific calculation change.

Compatibility risk is limited to unreferenced internal types and namespaces;
every removal must be preceded by caller, XAML/BAML, persisted-data, and
provenance checks.
