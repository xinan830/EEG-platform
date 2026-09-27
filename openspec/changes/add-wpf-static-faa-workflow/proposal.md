# Add WPF static FAA workflow

## Why

The backend exposes the official frontal Alpha asymmetry algorithm, but WPF
does not yet expose its two explicit source-channel inputs.

## Scope

Support static FAA with separate F3 and F4 source channel selection and an
explicit time range. Dynamic FAA remains unsupported because the backend
contract is static-only.

## Scientific boundary

The backend remains authoritative for Alpha power, logarithmic subtraction,
quality, units, and unavailable results. WPF only validates selections,
submits the Run, and displays the returned scalar.
