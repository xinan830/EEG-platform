# User Metric Runs and Results Design

## Goal

Turn a saved researcher metric such as `Theta 功率 ÷ Beta 功率` into a
traceable EEG-derived result without moving scientific calculation into Vue.

## Architecture

The new `definition_metric` AnalysisRun is a static scalar execution. It
resolves curated power features from one channel's offline-spectral-v3 result,
passes typed Scalars to the existing closed definition graph executor, and
persists output, units, quality, source range and input snapshot through the
existing queue and artifact path. Definition identity/version are captured in
the normal cache and provenance chain.

The ordinary algorithm-library view becomes the launch point: choose channel,
use the active analysis range, run, then view a compact result card. The
Results Workbench remains the durable place for full provenance and export.

## Display contract

One static scalar does not have a real continuous X axis, so it is displayed
as a number card. When inputs are compatible, a two-bar comparison visualizes
the measured source values with names on X and their backend-declared unit on
Y. The output's own unit remains fixed from the executor; the user cannot
rename it in the chart. A later time-window mode may display a line chart only
as `时间（s） → 指标值（persisted unit）`.

## Error behavior

Missing channel/definition/version/feature produces a stable structured run
failure. A quality-gated source spectrum produces `gate_failed`, null output,
and preserved quality evidence. Neither case emits a zero metric or a
misleading chart.
