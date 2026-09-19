# Design

The desktop captures and persists raw batches before dispatching them to a
bounded analysis bridge. The bridge posts batches to a localhost FastAPI
session which owns the existing causal SOS filter state and returns values in
V. Only reference and bipolar channels are filtered; counter and trigger
columns remain unchanged. The filtered batch is used for display only.

High-pass and low-pass configuration is accepted only before a live session
starts. Changing stateful IIR coefficients during a recording would mix two
filter histories, so it is explicitly disabled. The selected low-pass must be
below the actual stream Nyquist frequency.
