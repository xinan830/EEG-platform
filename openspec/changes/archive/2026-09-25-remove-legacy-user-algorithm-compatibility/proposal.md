# Remove legacy user-algorithm compatibility

## Why

The project no longer supports user-authored algorithms. The repository still
contains historical read endpoints, legacy result presentation, and test data
for that retired path. Keeping those paths active increases the chance that
new Runtime work accidentally depends on the old contract.

## What Changes

Remove the retired legacy analysis and Definition routes, remove the old
frontend result path, and purge retired user-algorithm database rows and
derived artifacts while preserving raw recordings and official Runtime data.

## Decision

Remove the retired user-algorithm execution and historical compatibility
surface. Preserve raw EEG recordings and official Runtime algorithms. A one-time
database migration removes test-era user Definitions, Definition Runs,
previews, legacy analysis rows, and their derived artifacts.

## Non-goals

- Do not delete raw recording files or recording metadata.
- Do not change official algorithm formulas, units, quality rules, or Runtime
  output contracts.
- Do not change acquisition, playback, event, or review behavior.
