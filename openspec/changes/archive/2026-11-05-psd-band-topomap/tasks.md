## Scientific contract and backend

- [x] 1.1 Add the canonical five band edges, including Gamma, to the scientific analysis contract and consume them from PSD band-share and RBP calculations.
- [x] 1.2 Replace the mistaken band-power field with `psd.band-mean-density.<band>`; divide the trapezoidal integral by full bandwidth, retain `V^2/Hz`, and reject the former power field.
- [x] 1.3 Add constant-density, unequal-bandwidth, nonuniform-grid, unit, channel-order, quality, invalid-range, negative-density and real HTTP response regressions.

## Review UI

- [x] 2.1 Replace the PSD Review frequency-bin selector with five named band options and explicit PSD band field IDs.
- [x] 2.2 Bind all five options to mean-density fields; label maps as average PSD; reject wrong-metric responses and verify the single SI-to-microvolt display conversion.

## Validation

- [x] 3.1 Run focused backend topomap/PSD/RBP tests (57 passed).
- [x] 3.2 Run focused desktop Review selector/presentation tests (14 passed); manual live WPF acceptance was not performed.
- [x] 3.3 Run strict OpenSpec validation and `git diff --check` on changed files.
- [x] 3.4 Assess the topomap service split by responsibility and record the follow-up boundary without a size-only refactor.
