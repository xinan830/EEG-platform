# Design

Reuse the existing registration, channel, time-range, Run polling, and scalar
summary path. Peak Frequency adds two frequency inputs only when selected. The
request carries the exact decimal values, `mode=static`, catalog algorithm ID,
and scientific version. The result summary displays the backend value in Hz,
quality, channel, range, and provenance; no structured matrix request is made.

The form rejects missing/nonfinite/negative bands, `low_hz >= high_hz`, and a
high edge at or above Nyquist before submission. Backend validation remains
authoritative. Tests cover config serialization, validation, scalar formatting,
and the existing PSD/STFT/RBP workflows.
