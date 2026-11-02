# Why

Montage details show electrode names without the bound physical input numbers,
making renamed electrodes difficult to trace to the saved source mapping.

# What Changes

- Add separate read-only positive and reference input-number columns.
- Resolve numbers from the montage's channel snapshot, including all selected
  average and specified-pair sources. Hardware REF has no sampled input number.
- Preserve serialization, formulas, acquisition and review compatibility.

# Impact

Only the montage draft presentation and detail table change. Missing or ambiguous
sources display unknown rather than guessing. No backend or raw data changes.
Existing oversized editor files receive a scoped size assessment; broader editor
extraction is deferred to avoid mixing lifecycle changes with presentation.
