# Design

The IAPF view-model parses `result_summary.metric`, never `ResultSummaryText`. Static values come from `output.value` (`Hz`) and `official.iapf_evidence`. Dynamic points come from `metric.series[*]`, with `time_s` and window boundaries in recording seconds. Only `Complete` points with finite numeric values enter the trend; missing or rejected points remain gaps. The current metric panel reflects the latest released window, including its unavailable state, rather than silently retaining an earlier valid value.

The existing catalog preview clock owns play, pause, seek, and step. For IAPF, it receives window boundaries directly from the completed metric series. The IAPF view-model filters those backend points by the clock cursor; SciChart renders them without calculating a scientific value. The chart occupies an IAPF-owned view file, not the shared page shell.

The backend provides selected Peak/COG scalar evidence but no Alpha residual spectrum array. The page therefore does not depict a residual spectral trace.

The existing shared `QualityStatusCard` also serves IAPF. Its status comes from the current backend point, and window counts, summary, and expanded failure reasons cover only windows released by the playback cursor. This keeps the visible counts consistent with the failure details and does not add a second IAPF-specific quality card.
