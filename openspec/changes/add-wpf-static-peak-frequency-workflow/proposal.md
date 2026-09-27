# Add WPF static Peak Frequency workflow

## Why

The backend exposes the official Peak Frequency algorithm, but WPF currently
does not submit its required frequency band parameters. This change adds the
static form and scalar result display without implementing spectral math in C#.

## Scope

Support one registered raw channel, an explicit static time range, and a
frequency band (`low_hz`, `high_hz`) for Peak Frequency. Dynamic Peak Frequency,
box selection, and other algorithms remain outside this change.

## Compatibility and scientific impact

The backend algorithm, frequency-grid tie policy, units, quality gates, and Run
schema are unchanged. WPF validates basic finite ordering and Nyquist bounds
for usability; the backend remains authoritative.

## Migration and rollback

The change is additive to the WPF form. Reverting it leaves existing Runs and
artifacts readable and does not alter recordings or scientific calculations.
