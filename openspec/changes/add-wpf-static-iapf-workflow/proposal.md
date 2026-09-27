# Add WPF static IAPF workflow

## Why

The backend exposes the official individual Alpha peak frequency algorithm,
but WPF does not yet allow operators to run it against a registered recording.

## Scope

Support one registered channel and an explicit static time range for IAPF.
Dynamic IAPF remains outside this change.

## Scientific boundary

The backend remains authoritative for Alpha residual processing, peak/COG
selection, quality, units, and unavailable results. WPF only submits the
contract and displays the backend scalar.
