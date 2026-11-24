# Design

## Navigation without a route migration

The product currently runs as one local workbench, not a multi-page application.
The change therefore introduces in-page task navigation that scrolls to stable
sections rather than inventing routes or a large administrative sidebar.

## Presentation modes

Ordinary mode is the default. It keeps the file, channels, waveform, frequency
and time-frequency workflows visible. Developer mode is explicit and only
reveals diagnostic transport/render output. Algorithm technical details already
remain opt-in in the algorithm workbench.

## Analysis boundary

Viewer controls must be labelled as display-only. Their reference and filter
settings are not the `offline-spectral-v3` analysis contract. This is a
presentation clarification and does not modify either pipeline.
