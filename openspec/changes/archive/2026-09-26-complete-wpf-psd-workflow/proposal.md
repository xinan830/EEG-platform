# Complete WPF PSD workflow

The initial WPF algorithm page proved the backend round trip but still requires
typing a raw directory and silently chooses the first EEG channel and full
recording range. It can also submit a non-PSD algorithm using that PSD-shaped
configuration. Complete the first product workflow around static PSD without
moving any scientific calculation into WPF.

## Scope

- Select a completed recording from the existing project catalog.
- Register it before showing its authoritative analysis channels and duration.
- Require an explicit channel and static time range before submitting PSD.
- Render the backend-returned PSD frequency series with axes and units.
- Keep other official algorithms visible but do not submit them through the
  PSD-only form.

## Out of scope

- Dynamic PSD, other algorithm parameter forms, real-time algorithms, and
  independent scientific validation of the PSD values.
