# Design

## Mode and configuration

The selected catalog item is the source of truth for supported modes and its
`dynamic_policy`. WPF exposes `静态` or `动态` only when the catalog declares
the mode. Dynamic runs send `mode`, `dynamic_window_s`, and `refresh_step_s` in
the same official algorithm config used by static runs. These names match the
public Run API; the backend maps them to the runtime module's `window_s` and
`step_s`.

The client validates positive values and range ordering for user feedback, but
the backend remains authoritative for scientific validation.

## Results

Dynamic scalar results are read from the backend `metric.series` summary. PSD
and STFT structured results are read through the existing bounded structured
preview endpoint. WPF only displays returned values, axes, units, window
states, and counts. Null/unavailable cells remain unavailable and are never
converted to zero.

## State and stale results

Changing algorithm, recording, mode, channel, or range clears the previous
dynamic result. A new run replaces the displayed result only after it reaches
the terminal state. Polling and preview loading use the existing API client
and cancellation boundary.
