# FAA Scientific Boundary

Scope is the entire immutable recording before requested-window slicing,
including for non-zero static starts. Filter is SciPy odd-padding zero-phase
50/60 Hz notch (when selected) followed by configured Butterworth bandpass.
The same continuous stream supplies both static and dynamic windows. Epoch
mean removal remains local, as does paired quality rejection.

Validation separately compares block filtering to full-array SciPy filtering,
new static to matching dynamic windows, and legacy per-window filtering to v2.
Independent FAA reference implements Hann density normalization, one-sided
scaling, clean-pair selection, inclusive Alpha integration and log-difference.
Record all window quality differences and max absolute differences; legacy
equality is not a requirement because boundary semantics intentionally change.

Output is dimensionless, raw signals V/float64, powers V^2; historical contracts
are not edited. Startup/end transients remain visible and subject to the same
quality gate; NaN gaps are not filled. Definition and cache identity change.
