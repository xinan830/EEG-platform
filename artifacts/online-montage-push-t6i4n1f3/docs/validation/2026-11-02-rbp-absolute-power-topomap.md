# RBP Absolute-Power Topomap Validation

Date: 2026-11-02

## Automated checks

- Backend RBP, result-contract, Definition, and topomap tests: 118 passed.
- WPF RBP on-demand, topomap, and Review regression tests: 42 passed. `dotnet test` rebuilt both the desktop application and test project. Coverage includes current ten-field results, historical relative-only results, partial absolute-field rejection, and power-selector labels.
- `openspec validate add-rbp-absolute-power-topomap --strict --no-interactive`: passed.
- `git diff --check`: passed.

The backend test run emitted two dependency deprecation warnings. The WPF build
emitted one existing xUnit analyzer warning in `OfflineAnalysisSelectionTests`.

## Manual acceptance

On 2026-11-03, the user confirmed that Review-page acceptance was complete and
reported no issues. The exact interaction sequence was not separately
recorded. Automated tests cover power-selector serialization, response parsing,
band selection, and historical relative-only result handling.

## Latency Follow-up

The desktop experiment log `C:/Users/23998/AppData/Local/BrainPlatform/logs/desktop-20261102.log` covered playback from about 18:14:42 to 18:15:29:

- 48 RBP results were bound. The client analysis queue reported `pending_count=0` at all 48 starts. End-to-end result binding had a 109 ms median and one 666 ms outlier at cursor sample 4000; this is not a backend-only compute measurement.
- Result-binding intervals had a 998 ms median, a 1,562 ms maximum, and 22 intervals over one second. That confirms some irregular output timing but does not identify the responsible layer.
- 34 topomap requests took a median 158.6 ms and a maximum 205.2 ms. Every request omitted `band_id` and returned all five 65x65 maps (21,125 grid cells). WPF prepared each map in roughly 8–11 ms and drew it in under 1 ms in the sampled entries.
- Playback ticks were mostly regular: the periodic summaries show about 8.6–8.9 ms median tick gaps and about 29–31 ms p95. There were occasional gaps, including one 211.9 ms maximum early in playback; the tick workload was usually below 1 ms, with a few larger outliers.

At the time this log was read, `storage/logs/backend-20261102.log` was empty. The experiment therefore cannot separate backend source preparation, RBP spectrum loading, band integration, worker-slot wait, or server response serialization. The desktop queue being empty rules out a growing WPF-side pending queue in this sample, but does not prove that the backend had no queue.

The RBP relative shares and absolute band powers are produced from the same spectrum and band integrations; absolute-power display does not start another PSD calculation or RBP run. The current UI requested all five maps, so the single-band optimization was not active in this experiment. The API keeps the five-band default and now accepts the selected band explicitly.

After this analysis, the focused backend integration tests wrote test traffic to `storage/logs/backend-20261102.log` (about 1.36 MB, ending around 18:51). That file was empty before the tests; entries after that point are test runs, not the user's playback experiment. The file was left intact.

### Follow-up Verification

- Focused backend RBP, topomap, API, Definition, and result-contract tests: 115 passed.
- Focused WPF request, Review adapter, presentation, and on-demand tests: 38 passed. The test build compiled the desktop application. It used a temporary output directory because the running desktop process locks the normal output files.
- `openspec validate add-rbp-absolute-power-topomap --strict --no-interactive`: passed.
- `git diff --check`: passed.

The new backend timings need a fresh playback run with this code before
attributing the observed delay to a backend stage.

### Final presentation acceptance

The user also confirmed the final topomap presentation: the band name is beside
and vertically centered against the head map, while the unit remains above the
color scale. A WPF build of this final presentation passed on 2026-11-03. A
subsequent focused test rerun could not rebuild because the running desktop
process held the default apphost executable open; the earlier focused WPF test
run recorded above passed.
