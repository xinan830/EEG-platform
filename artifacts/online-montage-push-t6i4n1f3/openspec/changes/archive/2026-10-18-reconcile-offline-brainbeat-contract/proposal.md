# Reconcile offline Brainbeat contracts

## Why

The installed Brainbeat manifest is available and runnable for static and
dynamic offline Runs. The official-brainbeat specification describes this
independent-window implementation, but three older specifications still
describe Brainbeat (and in two places FAA) as disabled. Format validation
cannot detect this semantic contradiction.

## What Changes

- Replace obsolete catalog cutover restrictions with installed-module availability.
- Preserve the distinction between independent-window offline Brainbeat and
  stateful realtime IAPF-lock/EMA behavior.
- Preserve existing scenario names and unrelated RBP/FAA scientific rules.

## Impact

Specification reconciliation only: no algorithm, API, version, cache identity,
unit, filter, window, raw recording, or WPF behavior changes. Evidence comes
from the Brainbeat manifest, runner, catalog tests, independent references,
and on-demand equivalence tests. Historical archives remain unchanged.
Rollback restores only these specification edits. All new files are below
400 lines. Existing uncommitted acceptance archives are outside this change.
