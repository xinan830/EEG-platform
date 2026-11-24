# Design

The official Run request uses `analysis_type=official_algorithm` and a
validated config containing `algorithm_id`, time range, mode, and optional
refresh/window settings. The executor loads the existing offline PSD for each
window, constructs the existing `SpectralEstimate`, and calls the official
module entry point. Static results contain the official scalar outputs and
spectral evidence. Dynamic results contain one persisted point per refresh
window with the real window bounds and quality state.

IAPF uses the requested analysis channel and returns Hz. Theta/Beta resolves
the recording's stored `ChannelMapping` (`fz`, `pz`, `oz`) and returns the
ordered per-role ratio values from the existing `metric_values` implementation.
If the mapping is absent or incomplete, the Run fails with a structured
`OFFICIAL_CHANNEL_MAPPING_REQUIRED` error; no positional fallback is allowed.

The registry remains the only source of availability. Definition records stay
immutable installation evidence. The official execution path is separate from
the generic user Definition graph so composite semantics cannot be simplified.
