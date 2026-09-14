## Why

The completed primitive layer can make safe calculations but cannot yet store,
version, validate, compare, or reproducibly execute a user-visible algorithm
definition. Without an immutable definition identity, a Run can only identify
the current implementation, not the exact graph and parameter schema that
produced a result.

## What Changes

- Add SQLite-backed `AlgorithmDefinition` identities and immutable published
  versions with SemVer, graph, parameter schema, input/output declaration,
  units, quality rules and scientific references.
- Add a closed-graph validator and executor over Change 02's `NODE_REGISTRY`.
- Add a restricted formula parser for constants, named inputs, parentheses and
  whitelisted arithmetic; no Python evaluation or imports.
- Add definition create, clone, validate, publish, get, list, and version
  comparison APIs. Published versions are append-only.
- Keep existing APIs and official results unchanged. This executor first serves
  only explicit preview/testing definitions.

## Out of Scope

No form builder, drag/drop DAG, official algorithm migration, arbitrary Python,
custom plugins, batch queue, or frontend scientific calculation is included.

## Risks

Graph validation must distinguish a malformed graph from a valid graph whose
data is unavailable. The former is a structured definition failure before a
Run; the latter is a typed unavailable result with quality provenance. SemVer
validation and immutable persistence add migration risk, so upgrade/rollback
tests are mandatory.
