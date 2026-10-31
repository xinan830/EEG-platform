## Purpose

This capability defines how registered official algorithm groups declare and execute child algorithms as a validated dependency graph while preserving each module's scientific inputs, state, failures, and result identity.

## ADDED Requirements

### Requirement: Declare a versioned closed algorithm dependency graph

An official algorithm group SHALL declare a versioned graph of registered child algorithm nodes. Each node SHALL declare its stable module identity, dependencies, ordered input roles, input representation, sample-window policy, update cadence, state ownership, preprocessing identity, output unit, failure behavior, and parallel-safety policy. The graph SHALL NOT load arbitrary executable definitions from user data.

#### Scenario: Register a valid algorithm group
- **WHEN** an official group is registered with unique nodes and resolvable acyclic dependencies
- **THEN** the runtime accepts the group graph and exposes the group's existing public catalog identity
- **AND** internal child nodes remain non-selectable unless explicitly registered as public algorithms

#### Scenario: Reject an invalid graph
- **WHEN** a graph contains a duplicate node, missing dependency, self-edge, cycle, invalid role/unit declaration, or unsafe concurrency declaration
- **THEN** registration fails with a structured graph-validation error
- **AND** the runtime does not fall back to a fixed-order child loop

### Requirement: Preserve signal, time, and preprocessing contracts per node

The group scheduler SHALL consume prepared typed inputs from the existing algorithm input boundary and SHALL NOT read recordings or perform undeclared preprocessing. Signal values SHALL remain sample-major V/float64 with explicit channel names/order and recording-absolute half-open sample bounds. Public result time SHALL remain recording-relative seconds, and each node SHALL retain its declared output unit and preprocessing provenance.

#### Scenario: Prepare a due child node
- **WHEN** a child node is due for a group frame
- **THEN** it receives only its declared roles and exact sample window under its declared preprocessing contract
- **AND** source, sample bounds, channel order, unit, and preprocessing identity remain available in evidence

#### Scenario: Child is not due
- **WHEN** a group frame falls outside a node's warm-up or update cadence
- **THEN** the node returns a terminal not-due or insufficient-context result with a structured reason
- **AND** the scheduler does not compute it or fabricate a numeric value

### Requirement: Schedule dependency-ready nodes with bounded concurrency

The group scheduler SHALL execute nodes only after all declared dependencies have reached terminal outcomes. Required dependency failure SHALL block only its dependent node with an explicit failure; optional dependencies SHALL provide unavailable context without suppressing an otherwise independent calculation. The scheduler MAY run independent nodes concurrently only when each node declares parallel safety, its input is prepared, and the group concurrency limit is respected. Stateful nodes SHALL have one writer per node and SHALL process frames in ascending sample order.

#### Scenario: Run independent parallel-safe nodes
- **WHEN** multiple due nodes have no unresolved dependencies and declare parallel safety
- **THEN** the scheduler may execute them concurrently up to the group's fixed worker limit
- **AND** their shared input preparation, state ownership, and result collection remain race-free

#### Scenario: Continue after one node's scientific failure
- **WHEN** a node returns a structured unavailable, rejected, or partial result
- **THEN** independent nodes continue to their terminal result
- **AND** only dependents requiring the failed output are blocked; optional dependents receive the failed result as context

#### Scenario: Preserve ordered stateful replay
- **WHEN** a stateful group is evaluated over multiple frames or replayed to a target frame
- **THEN** each node's state advances once per due frame in ascending sample order
- **AND** checkpoint identity includes the active graph and implementation identity

### Requirement: Publish complete deterministic group frames

The group SHALL publish a frame only after every declared node has a terminal outcome for that frame. Result/evidence order SHALL follow the group's declared output order rather than task completion order. Every child result SHALL retain its own value, unit, quality, failure, actual window, and evidence within the existing full or on-demand result contract.

#### Scenario: Publish results after parallel execution
- **WHEN** dependency-ready child tasks finish in a different order from their declaration
- **THEN** the published group result still uses the declared stable metric and channel order
- **AND** no frame is partially published or silently omits a failed child

#### Scenario: Publish a partially available frame
- **WHEN** some children are valid and other children have structured failures or are not due
- **THEN** the group preserves every child outcome and nulls unavailable values
- **AND** the group quality/state reflects the child outcomes under the current result contract

### Requirement: Separate scientific identity from presentation generations

The group graph version and implementation identity SHALL participate in scientific result, cache, and replay-checkpoint identity. UI view/request generations SHALL only reject stale presentation responses and SHALL NOT change scientific task identity or result values.

#### Scenario: Reuse a child or group result
- **WHEN** a cached result was computed using a different graph, module, preprocessing, configuration, source, or exact sample-window identity
- **THEN** the scheduler rejects reuse and computes under the active graph or returns a structured unavailable result
- **AND** a different UI view generation alone does not alter scientific cache identity
