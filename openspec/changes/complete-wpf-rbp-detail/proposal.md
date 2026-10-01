# Complete WPF RBP Detail

## Why

The official RBP backend returns five relative band powers (Delta, Theta,
Alpha, Beta and Gamma) for current static and dynamic Runs, but the
WPF-specific detail view is still a generic placeholder. Dynamic RBP is
currently reduced to text and is not connected to the shared time cursor.

## Scope

- Render backend-returned Delta, Theta, Alpha, Beta, and Gamma shares in an RBP-owned
  SciChart view, with the declared ratio unit and source window.
- Show a five-band dynamic trend synchronized with the existing draggable time
  cursor and distinguish partial, complete, rejected, and unavailable windows.
- Reuse the shared execution, quality, provenance, and timeline controls.

## Non-goals

- No RBP formula, quality-gate, filtering, or backend result-contract changes.
- No client-side PSD, band integration, or normalization.
- No live acquisition or recording-review integration.
