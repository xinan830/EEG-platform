# Official algorithm foundation and parameter contract

## Why

The platform is moving from user-authored executable algorithms to a curated
official algorithm catalog. The current runtime already has versioned modules,
parameter schemas, Runs, and provenance, but the scientific foundation and the
boundary between algorithm, preset, execution, and display state are not yet
specified as one contract.

## Scope

- Add optional minimum, maximum, and step constraints to the backend parameter
  schema.
- Enforce declared enum and numeric constraints in the shared runtime before
  algorithm execution.
- Declare constraints for the current official algorithm parameters and expose
  them through the existing algorithm catalog.
- Add local parameter preset CRUD backed by the same runtime validation.
- Define the official-only module boundary and the reusable scientific
  primitive layer for spectral and time-frequency analysis.
- Define preprocessing, quality, static/dynamic execution, gap handling, and
  result evidence contracts.
- Freeze SI internal units and recording-relative sample coordinates before
  implementing additional primitives.
- Define the migration boundary for historical user-defined algorithms.
- Add focused contract tests before implementation work for each module.

## Non-goals

- No new scientific algorithm or formula.
- No user-defined executable code.
- No immediate exposure of every internal preprocessing parameter to users.
- No change to existing result units or time semantics without a new scientific
  version and migration evidence.
- Display settings such as timebase, paper speed, sensitivity, and chart state
  are not scientific algorithm inputs.
- No implicit V-to-uV conversion inside a scientific primitive.

## Compatibility and migration

Existing clients may ignore the new optional schema fields. Existing valid
requests remain valid. Invalid values now fail before queue execution with a
structured validation error. Presets are local convenience resources only;
every Run still stores its full configuration and scientific version
independently. Existing historical user-defined Runs remain readable; new
user-defined execution will be disabled only after a separate migration step
has been verified.
