# Why

The catalog Run flow contains concrete algorithm checks and result-dispatch
branches. Adding a detail requires editing orchestration instead of providing
an algorithm-owned adapter.

# What Changes

- Introduce a closed registry of eight algorithm-owned WPF run adapters.
- Move algorithm-specific input checks, preview descriptions and scalar window
  parsing into their corresponding algorithm directories.
- Keep common scheduling, request serialization and playback state in the
  catalog. Preserve existing request values, units and quality semantics.

# Impact

WPF ViewModels and tests only. No backend API, scientific version, numerical
processing, acquisition or chart-style changes. Existing pending algorithm
detail changes remain in the working tree.
