# Design

`filter_low_hz` and `filter_high_hz` identify the continuous preprocessing
bandpass. `output_low_hz` and `output_high_hz` identify the frequency bins
returned to the caller. The preprocessing cache identity includes only filter
and notch settings; output clipping never creates a different filtered signal.

The existing `low_hz`/`high_hz` service arguments remain accepted as a
transitional alias mapping both filter and output ranges. Official PSD uses
the explicit names and records both ranges in evidence.
