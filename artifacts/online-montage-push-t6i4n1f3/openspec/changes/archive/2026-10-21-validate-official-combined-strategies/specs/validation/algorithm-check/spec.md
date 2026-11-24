## ADDED Requirements

### Requirement: Validate combined execution and directional reuse separately

The platform SHALL mark a combined strategy or reuse direction validated only
when algorithm-scoped replayable engineering evidence exercises that path.
Combined validation SHALL compare overlapping preview/background execution to
uncached execution with the same source, active scientific identity, channel
order, preprocessing, units and actual sample bounds. It SHALL compare arrays,
quality, intermediate evidence and provenance with explicit tolerances and
report failed coordinates. A frozen lifecycle summary alone SHALL NOT establish
combined parity. Unsupported reuse directions SHALL remain explicit.

#### Scenario: Validate overlapping preview and background execution

- **WHEN** an official algorithm is validated for combined execution
- **THEN** evidence demonstrates overlapping preview and background lifecycles, independent cancellation and matching numerical results for the same scientific windows
- **AND** unavailable results retain their structured reason and null numerical value

#### Scenario: Validate on-demand result reuse by full analysis

- **WHEN** a full analysis consumes a previously computed on-demand window
- **THEN** evidence confirms actual reuse and compares the result with a full analysis computed without that cached window
- **AND** incompatible identity or invalid evidence triggers recomputation rather than reuse

#### Scenario: Declare a new algorithm without evidence

- **WHEN** a newly registered algorithm has no algorithm-scoped combined evidence
- **THEN** its combined validation status remains unvalidated regardless of other algorithms passing shared lifecycle tests
