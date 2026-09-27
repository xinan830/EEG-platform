# Add WPF static Band Ratio workflow

## Why

The backend exposes the official Band Ratio algorithm, but WPF does not yet
provide its explicit numerator and denominator frequency bands.

## Scope

Support one registered channel, an explicit static time range, and two valid
frequency bands. Dynamic Band Ratio and box selection remain outside this
change.

## Scientific boundary

The backend remains authoritative for PSD, band integration, units, quality,
and unavailable results. WPF only validates input usability, submits the
contract, and displays the backend scalar result.
