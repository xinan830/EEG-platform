# Design

Use a workspace-owned `RecordingTimeDisplaySettings` and a recording-specific
`RecordingTimeDisplayContext`. Each context receives an explicit start UTC from
the immutable review manifest or the local recording catalog. Context changes
only notify display properties, never execution or cache invalidation.

Shared formatting retains relative seconds as the underlying coordinate and
uses `RecordingTimeMapper` for clock conversion. A day rollover includes the
date. Missing origins disable clock selection and fall back explicitly to
relative display. Duration values remain durations in either mode.

SciChart time axes opt into an inherited presentation context. Frequency axes
and category axes do not. Mixed charts detach time formatting when returning
to a frequency/category view. Label invalidation does not rebuild DataSeries.
Review uses recording-aligned second ticks in both modes. Switching changes
labels only, never grid coordinates; a fractional start of 10:47:21.014 labels
the 1-second grid at 10:47:22.014, not 10:47:22.000. Clock axis labels include
milliseconds. Event/lifecycle positions never move. Live acquisition retains
its existing wall-clock tick policy.

Range inputs keep seconds and an explicit unit label. Shared result range and
dynamic playback text use the display context. Scientific evidence text keeps
its original units/coordinates. Settings are in-memory, with no backend or
recording persistence writes. Existing recordings need no migration.

Tests cover origin/missing metadata, fractional seconds, midnight, long
durations, identical samples/ranges, chart mode switches, shared preference,
unchanged playback/requests and UI bindings. Validate WPF tests/Release build,
OpenSpec and diff whitespace. Manual GPU/window acceptance remains separate.
