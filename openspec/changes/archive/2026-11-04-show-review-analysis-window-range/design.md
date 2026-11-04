## Context

On-demand dynamic responses carry actual `window_start_s` and `window_end_s`; topomap responses carry their own actual window boundaries. The PSD view model already formats the spectrum response range, and RBP already formats the topomap range and current result window, but the labels are not positioned consistently on each visualization.

## Decision

Expose a display-only current range string from the PSD/RBP presentation view models and bind it as a small lower-left overlay within each chart surface. Bind PSD spectrum to its current PSD window and PSD topomap to its topomap response. Bind each RBP history chart to the current RBP result window and the RBP topomap view to its topomap response. Use backend response boundaries only; missing boundaries produce an unavailable label.

## Risks and Compatibility

This changes only WPF presentation. It does not change scientific requests or results. The label may overlay chart whitespace, so the visual check must confirm it does not obscure axes or data at narrow panel sizes.

## Validation

Add focused view-model tests for formatted ranges and missing-range behavior, run PSD/RBP Review tests and a WPF build, then run strict OpenSpec validation and `git diff --check`. Manual live playback remains a separate acceptance check if no suitable recording session is active.
