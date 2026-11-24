## 1. Lock contracts and tests

- [x] 1.1 Add backend contract tests for absolute output names, `uV^2`, channel order, unavailable values, and static/dynamic/on-demand shapes while asserting relative values remain unchanged.
- [x] 1.2 Add topomap service/API tests for relative default, explicit absolute selection, response representation/unit, and structured unavailability when stored output lacks absolute arrays.
- [x] 1.3 Add WPF tests for the independent power selector and serialization of the selected representation through the current-window topomap request.

## 2. Publish RBP absolute outputs

- [x] 2.1 Extend the shared RBP frame with five per-channel absolute-power arrays sourced from existing band-power integration; declare units for static, dynamic-series, and on-demand results.
- [x] 2.2 Update the RBP Definition output metadata and implementation identity while preserving `official-rbp-v4`; validate structured-result shapes and unavailable channels.
- [x] 2.3 Verify the same arrays and units are declared by the RBP manifest and all runtime output paths.

## 3. Select power representation in topomap

- [x] 3.1 Add the closed `relative`/`absolute` selector to run and dynamic topomap requests and include the selected representation in responses.
- [x] 3.2 Make the RBP adapter select only the declared structured arrays, returning a stable unavailable result when absolute arrays are absent; never read calculation-trace values as fallback.
- [x] 3.3 Add a WPF power-type selector separate from the frequency-band selector and reload the same active window without creating or recomputing an AnalysisRun.
- [x] 3.4 Update WPF RBP window parsing to validate complete new absolute arrays, preserve historical relative-only windows, and display power selector labels.

## 4. Verify and close

- [x] 4.1 Run focused backend RBP, result-contract, and topomap tests.
- [x] 4.2 Build the WPF desktop client and run focused topomap/Review presentation tests.
- [x] 4.3 Run `openspec validate add-rbp-absolute-power-topomap --strict --no-interactive` and `git diff --check`; record results and remaining manual acceptance limits.
- [x] 4.4 Manually verify relative/absolute switching, units, frequency-band selection, and that the same active playback window is shown in the Review UI.

## 5. Reduce and diagnose per-window topomap work

- [x] 5.1 Add the optional closed `band_id` filter to run/dynamic APIs and backend adapters; preserve the five-band default and test that a selected band alone is interpolated.
- [x] 5.2 Pass the selected band through the WPF request path and refresh the same active window when the band selector changes; test query serialization and presentation.
- [x] 5.3 Add request-correlated backend timings for RBP spectrum loading/band integration and topomap per-band interpolation/response serialization, reusing existing source-preparation timings.
- [x] 5.4 Run focused backend and desktop tests, build WPF, run strict OpenSpec validation and `git diff --check`, then record the validation results.
