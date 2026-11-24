# Desktop Device Driver Registry Validation

Date: 2026-09-18

## Behavior Locked

The configured acquisition runtime selects only explicitly installed drivers.
It no longer constructs the ANT/eego adapter directly. ANT/eego remains the
first registered driver and retains its existing SDK path, range validation,
native stream lifecycle, sample-counter, and raw V/float64 behavior.

## Automated Verification

- `dotnet test desktop-client/BrainPlatform.Desktop.slnx -c Release --no-restore`:
  64 passed.
- `dotnet build desktop-client/BrainPlatform.Desktop.slnx -c Release --no-restore`:
  succeeded with 0 warnings and 0 errors.
- `openspec validate add-desktop-device-driver-registry --strict --no-interactive`:
  valid.
- Scoped `git diff --check`: no whitespace errors.

## Scope Boundary

No other vendor SDK has been declared supported. A future device requires its
own driver implementation plus hardware acceptance for channel order, V units,
sample-counter behavior, trigger/impedance support, gaps, reconnects, and raw
recording integrity.
