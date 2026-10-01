# Complete WPF IAPF Detail

## Why

The official IAPF Run already returns a scalar in Hz, method evidence, and a dynamic metric series, but its WPF detail view is still a generic placeholder. Operators cannot inspect the selected window's value and method or see the dynamic result against recording time.

## Scope

- Replace the IAPF placeholder with a dedicated result view.
- Display backend-owned IAPF, Peak/COG evidence, quality, and unavailable reason.
- Show the backend dynamic series through a SciChart time trend, synchronized with the existing analysis timeline.

## Non-goals

- No client-side IAPF, PSD, 1/f fitting, interpolation, or Alpha residual curve.
- No change to the official IAPF scientific contract or backend result shape.

## Validation and Risk

Unit tests cover static and dynamic parsing, null/rejected windows, time gating, and clearing stale results. Build and OpenSpec strict validation are required. WPF visual acceptance remains manual. This changes presentation only; Run identity, channel, time and units remain backend-owned.
