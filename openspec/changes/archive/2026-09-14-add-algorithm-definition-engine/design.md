## Context

Change 02 supplies typed values and a closed primitive-node allowlist. This
change adds an immutable algorithm definition layer above it without changing
the current `offline-spectral-v3` implementation path.

## Decisions

- Store one mutable definition identity and immutable version rows. A published
  version cannot be PATCHed or deleted; correction means a new SemVer version.
- Store canonical JSON graph/config/schema and digest it with `sha256_json()`.
  The digest covers parsed parameters, graph topology, reference/filter/channel
  mapping and quality rules.
- Graph is `{nodes, edges, outputs}`. Nodes use generated stable IDs, a closed
  registry name, declared input bindings and JSON parameters. Execution performs
  duplicate-ID, unknown-node, missing-input, cycle, unit and kind checks before
  node execution.
- Parameters are validated using a constrained JSON-schema subset: object,
  required, enum, number/integer bounds, string, boolean, and array. Unknown
  schema keywords fail validation rather than being silently ignored.
- Formula support uses Python `ast.parse(..., mode="eval")` only as a parser;
  accepted AST nodes are names, numeric constants, parentheses, `+ - * /`, unary
  signs and whitelisted `ln`. Attribute access, calls other than `ln`, indexing,
  comprehensions, strings and imports are rejected. Evaluation maps directly to
  primitive math functions; it never invokes `eval` or `compile`.
- New API and services are separate from `runs.py`; Run integration is limited
  to an explicit definition reference after publish. Existing spectra stay on
  their legacy path until Change 04 shadow evidence passes.

## Data Model

`algorithm_definitions`: id, name, owner, status, description, created/updated.

`algorithm_definition_versions`: id, definition_id, semver, lifecycle state,
graph JSON, parameter schema JSON, I/O JSON, unit JSON, quality JSON, reference
JSON, canonical digest, created timestamp and publication timestamp. A unique
constraint applies to `(definition_id, semver)` and `(definition_id, digest)`.

## Failure Semantics

`DEFINITION_INVALID`, `UNKNOWN_NODE`, `GRAPH_CYCLE`, `UNIT_MISMATCH`,
`MISSING_INPUT`, `PARAMETER_INVALID`, `FORMULA_INVALID`, `DIVISION_BY_ZERO` and
`MISSING_CHANNEL` are stable codes. Structural errors are never turned into
zero values. A quality failure remains a primitive unavailable value.

## Verification

Use synthetic typed inputs to test acyclic execution, cycle rejection, unknown
nodes, bad units, missing channels, divisor zero, formula allowlist, SemVer,
published immutability, canonical digest stability, migration idempotency and
API error envelopes. No real EEG is committed.
