# Improve algorithm-library readability and safe deletion

## Why

User-created research metrics are already stored as immutable algorithm
definitions, but their library cards currently expose an internal status rather
than the selected inputs, operation, and output. This makes a simple saved
metric hard to review without understanding the developer graph.

## What Changes

- Show saved private inputs, operation, output, and unit in the normal view.
- Permit confirmed removal of unreferenced private definitions.
- Preserve official and run/batch-referenced definitions as immutable
  provenance records.

This change does not alter the EEG calculation contract, Definition versions,
or official algorithms.
