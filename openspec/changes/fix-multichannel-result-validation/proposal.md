## Why

The shared result validator rejects current structured algorithms whose output contract declares multiple source roles. TNK declares five roles and returns a five-channel structured result, but validation currently requires one role.

## What Changes

- Validate requested channel count against the declared output contract.
- Preserve exact scalar channel-string and structured channel-order checks.
- Add regression coverage for TNK multi-channel structured results.

## Impact

This changes runtime validation only. It does not change scientific formulas, units, windows, or channel mapping.
