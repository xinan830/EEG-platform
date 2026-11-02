# Instrument-style EEG workspace layout

## Why

The approved first layout preview favors compact scientific-instrument surfaces:
menus in the window caption, a grouped action strip, labeled parameter fields,
and an edge-to-edge waveform. Current review has a separate menu row and an
overwide mixed settings/actions row; acquisition duplicates local button styles.

## Scope

- Move page-owned menus into the shared EEG session window caption.
- Apply shared opt-in instrument styles to acquisition and review only.
- Separate actions, waveform parameters, record information and transport.
- Keep existing command bindings, settings choices and state-dependent permissions.

## Non-goals and compatibility

No backend, filters, defaults, SDK, time mapping, channel ID compatibility,
recording files, waveform rendering, caching, or session lifecycle changes.
No dummy STFT/report/export actions copied from the HTML preview. Existing
unavailable actions remain unavailable. Other platform pages retain their styles.
No persisted-data migration; revert this presentation change to roll back.
