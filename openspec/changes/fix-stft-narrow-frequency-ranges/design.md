# Design

The requested low/high edges currently describe preprocessing and returned
frequency range. They remain unchanged. The supplementary band statistics use
the same filtered signal and FFT, not unfiltered EEG or a second transform.
Internally retain the union of requested frequencies and standard band edges,
capped strictly below Nyquist. Integrate before applying the response mask.
Do not clip a standard band into a different band or extrapolate missing bins:
its unavailable values are null when its full boundaries are not covered.

The returned matrix remains time x requested-frequency (or dynamic window x
inner-time x requested-frequency), V^2/Hz in official artifacts. Bad time rows
stay NaN; quality evidence and absolute times are unchanged.

Dynamic exception paths must use the same requested-frequency mask as successful
windows. A later clean window must not fail due to a fallback 1-50 Hz axis.

This is an implementation fix. Increment the STFT implementation identity, not
the scientific version. Read stored historical artifacts without recalculation.

Validate static/dynamic narrow and fractional ranges, independent primitive
matrix equality, complete band integrals, below-30 Hz Nyquist support, and a
quality-rejected initial window followed by a clean window. Record real-source
checks separately from synthetic regression tests and desktop acceptance.
