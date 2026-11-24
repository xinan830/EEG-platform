# Design

Reuse WPF registration, channel selection, range validation, Run polling, and
scalar result display. Band Ratio adds four frequency inputs when selected:
numerator low/high and denominator low/high. Each band must be ordered and
both upper edges must be below the registered recording Nyquist frequency.
No spectral computation or client-side ratio is introduced.
