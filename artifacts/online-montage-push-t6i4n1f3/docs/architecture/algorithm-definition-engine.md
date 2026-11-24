# Algorithm Definition Validation

Definitions store versioned backend research metadata. Each published version
is immutable; updates require a new version rather than rewriting graph,
schema, unit, quality or digest fields.

`backend/app/definitions/validation.py` validates graph topology and parameter
schemas for DefinitionService. It rejects unknown primitive names, missing
dependencies, cycles, invalid outputs and parameter violations with structured
codes. Validation checks the closed primitive vocabulary but does not execute
nodes or process EEG.

The old graph executor, scalar AST formula interpreter and their compatibility
entry points have been removed. The scalar preview API was already retired;
there is no supported replacement formula execution path. Current official
algorithms run through their own modules and the shared algorithm runtime.
