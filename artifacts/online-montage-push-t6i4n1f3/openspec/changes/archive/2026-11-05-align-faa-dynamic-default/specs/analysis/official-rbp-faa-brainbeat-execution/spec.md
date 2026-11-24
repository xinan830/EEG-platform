## ADDED Requirements

### Requirement: Use the declared FAA dynamic window default

Dynamic Official FAA requests that omit `dynamic_window_s` SHALL use the FAA manifest's 20-second default. Requests that explicitly provide a positive window SHALL preserve that value. This default SHALL not change other algorithms' defaults.

#### Scenario: Dynamic FAA request omits its window

- **WHEN** a dynamic Official FAA request omits `dynamic_window_s`
- **THEN** its runtime configuration uses a 20-second analysis window and the declared refresh-step default
- **AND** the resulting window can contain the minimum 10 paired 2-second epochs required by the existing quality gate

#### Scenario: Dynamic FAA request provides a window

- **WHEN** a dynamic Official FAA request explicitly provides a window
- **THEN** the runtime uses the requested window without replacing it with the default
