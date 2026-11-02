# Design

Register two distinct source identities: legacy `wpf:` raw input and
`wpf-montage:` acquisition snapshot projection. WPF explicitly requests
`signal_mode=acquisition_montage`; API callers omitting it retain raw semantics.
Separate identities prevent cached results from mixing raw and derived sources.

A pure snapshot parser checks all dependencies against channel configuration and
the actual manifest native mapping. A bounded reader projects requested outputs
in V/float64, preserving NaN gaps. REF copies its source; Channel subtracts one
negative; Mean and SpecifiedPair subtract the saved reference group's mean.
Every algorithm uses the shared sample-reader boundary. Source formulas,
snapshot fingerprint and reference metadata are retained in execution provenance.

Tests cover numerical projection, ordering, missing metadata, contradictory
mapping, gaps, identity isolation, registration request serialization and full
desktop regression. No UI arithmetic is introduced.
