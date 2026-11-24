# Add WPF static RBP workflow

## Why

The backend already exposes the official Relative Band Power (RBP) algorithm,
but the WPF page only permits PSD and STFT. Selecting RBP must produce a
readable scalar/band result instead of an incorrect matrix-preview error.

## Scope

Allow a completed registered recording, one backend-declared channel, and an
explicit static range to run RBP. Display the four backend-returned band shares,
their ratio unit, quality, provenance, and Run identity. Keep dynamic RBP and
other scalar algorithms out of this change.

## Compatibility and scientific impact

The backend formula, units, quality gates, persisted Run schema, and artifacts
are unchanged. WPF performs no band-power calculation and does not convert
units. PSD and STFT rendering remain unchanged.

## Migration and rollback

This is additive WPF behavior. Reverting the change only removes RBP submission
and display; existing Runs remain readable through the backend.
