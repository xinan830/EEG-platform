# Remove Retired User-Definition Executor

## Why

The structural migration has moved the user-definition graph and metric
executor behind explicit `app/legacy` boundaries. Public creation and Runtime
execution are retired, but internal compatibility queues and historical tests
still require the executor to be importable.

## Goal

Remove the retired executable implementation only after all internal legacy
queue paths are converted to read-only historical evidence paths and no active
caller imports the executor.

## Scope

- identify and migrate remaining internal `definition_metric` execution paths;
- preserve historical Definition, Run, result, provenance, and artifact reads;
- remove the legacy executor and its compatibility facade;
- retain stable retired catalog and API responses;
- add architecture tests proving no executable user-definition path remains.

## Non-goals

- no change to official algorithms;
- no deletion or rewriting of historical database rows or artifacts;
- no change to scientific formulas or sample coordinates.

## Preconditions

- all historical-read tests pass without constructing `UserDefinitionAlgorithm`;
- queue recovery tests no longer execute `definition_metric` internally;
- a separate migration report records the final rollback point.
