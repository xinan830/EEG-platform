# Review scheduler performance protocol

This protocol is for one registered recording, one selected recording montage, one fixed sample rate and one fixed playback speed. It separates WPF rendering from backend analysis and does not treat engineering timing as clinical validation.

Enable opt-in diagnostics before starting the desktop client:

    $env:BRAIN_REVIEW_PERF_DIAGNOSTICS = "1"

Run four conditions without seeking during the measured interval:

| Condition | Waveform | RBP/PSD chart | Backend scientific work |
| --- | --- | --- | --- |
| A | on | off | off |
| B | on | on with static/cached data | off |
| C | on | on with simulated result delivery | off |
| D | on | on with live RBP | on |

For each condition, record at least 30 seconds after the initial load. Keep recording, montage, visible duration, playback speed and window step identical.

Collect:

- waveform tick interval P50/P95/P99 and maximum gap;
- WPF Dispatcher delay and SciChart update duration;
- scheduler queue depth and latest_cancel_active count;
- raw read count, preparation cache hit/miss and prepared sample range;
- RBP queue wait, compute duration, publication time and result lag relative to the cursor;
- published window count, missing/rejected window count and history coverage;
- CPU and memory usage.

Interpretation:

- If B is already slower than A, investigate multiple SciChart surfaces, data binding and Dispatcher/render priority first.
- If C is slower than B, investigate result collection notifications, chart projection and batch updates.
- If D is slower than C, investigate Python computation, IPC, cancellation release and CPU/thread contention.
- If D has normal UI timing but missing RBP windows, investigate scientific window planning, generation fences and ordered result merge.

A run is not an acceptance pass when only the chart looks continuous. The recording identity, montage/channel order, sample rate, window bounds, queue metrics and result coverage must also be captured.

## PSD/RBP and custom-frequency latency trace (2026-11-06)

Restart the desktop client and backend using the updated build before collecting a new run. Set `BRAIN_REVIEW_PERF_DIAGNOSTICS=1` in the process environment before launching the desktop client; it enables the additional desktop timings. Backend admission/worker/API summaries use the existing `INFO` logging level. No global operating-system setting is required. Keep the normal PSD/RBP analysis-window settings and collect both the standard panel and custom-frequency panel under the same recording, montage, channels and playback speed.

Logs remain in `%LOCALAPPDATA%/BrainPlatform/logs/desktop-YYYYMMDD.log` and `storage/logs/backend-YYYYMMDD.log`. Collect both for the same session and time interval, including first panel open and at least 30 seconds of uninterrupted playback. Then collect one single-bin drag and one range change; record when those actions occurred.

| Event | What it measures |
| --- | --- |
| `dynamic_window_admission_perf` | `request_planning_ms` before entering the shared admission lock; `lock_wait_ms` waiting for it and `lock_held_ms` inside it; aggregate `recording_metadata_ms`, `montage_catalogue_ms`, `montage_catalogue_cache_hits`/`montage_catalogue_builds`, snapshot row count, database connection count/time. |
| `dynamic_window_worker_started` | `executor_queue_ms` from submission until the executor starts this job; separate from existing scientific `compute_slot_wait_ms`. |
| `dynamic_window_worker_perf` | Aggregate `cancellation_check_ms` and atomic `cancellation_state_read_calls`/`cancellation_state_read_ms`, plus connection counts during one owned worker execution. The worker keeps one autocommit read connection; every check still queries live window and lease state. |
| `rbp_frame_perf` | Existing inclusive `band_integration_ms`, now separated into `band_formula_ms` and `band_check_ms`/`band_check_count`. |
| `dynamic_window_api_perf` | Service, audit write, response JSON conversion/encoding, and encoded response bytes. Carries HTTP `request_id` and business `analysis_request_id`. |
| `dynamic_subscription_release_perf` | Release service and audit write durations, with subscription/window identity. |
| `custom_frequency_bins` / `custom_frequency_range` | Projection `service_ms`, `response_encode_ms`, response bytes and total time, with both source window IDs. |
| `review_analysis_wait_perf` | Desktop target queue and algorithm-resource slot waiting. |
| `http_completed` | Time to **response headers**, explicitly marked `stage=response_headers`; response body is not included. |
| `http_response_body_perf` | Bytes consumed, aggregate stream-read wall time, body plus JSON time, and wall time outside reads. Streaming remains streaming; diagnostics do not buffer the full body. |
| `review_analysis_delivery_perf` | First terminal result observed by the desktop, awaited subscription release duration/outcome, and complete observation duration. |
| `review_analysis_binding_perf` / `review_custom_frequency_apply_perf` | Delay from task completion until the caller resumes, followed by binding/application duration; the latter also records playback position and applied result lag. |
| `review_custom_frequency_model_perf` | Validation, curve notification and table-row notification durations. Synchronous subscribers are included. |
| `review_custom_frequency_chart_perf` | Applying points to the existing SciChart series. |
| `review_custom_frequency_render_callback_perf` | Time from series update to the next WPF rendering callback. |
| `review_spectral_pair_perf` | Full source/projection cycle, actual PSD/RBP window bounds/states, playback position, next target, and `window_lag_s = playback_position_s - psd_end_s`. |

Join the ensure HTTP request to backend admission using `analysis_request_id`; join worker and delivery using `window_id`/`request_id`. Projection HTTP requests carry `psd_window_id` and `rbp_window_id`, which join them to the pair publication and UI apply events. `review_session_id` identifies the desktop review session where available. IDs are correlation metadata; EEG arrays, formulas, parameter payloads and lease tokens are not written to these logs. Channel labels and shape counts are the only chart metadata.

Interpretation limits:

- Timings are wall time, not CPU time. `json_nonread_ms` includes parsing and scheduling outside stream reads; `continuation_delay_ms` includes task/dispatcher scheduling and is not an isolated Dispatcher measurement.
- Stage timings are nested. Catalogue/snapshot/connect time is included in metadata time; database reads are included in cancellation checks. Do not sum inclusive fields as if they were independent stages. Missing fields mean the stage was not executed or no diagnostic scope was active.
- `database_connect_calls` counts successful connections through the common connection factory, not SQL statements; connection time includes setup PRAGMAs. Cancellation window/lease state now uses one combined query per checkpoint; older traces used separate `cancellation_window_read` and `cancellation_lease_read` fields. Snapshot row count is the number scanned, before catalogue deduplication; a warm validated catalogue still checks every saved row's complete identity and the manifest content hash.
- Worker `outcome` reports whether the traced invocation raised. Scientific ready/rejected/failed/cancelled state remains authoritative in lifecycle events; handled failures can return normally from the worker.
- A terminal result is when the desktop observes it, potentially after polling delay. Backend `dynamic_window_ready` is the publication event. Encoded response bytes and bytes consumed by the desktop do not measure packet overhead or prove delivery to the UI.
- Notification/series-update timings do not measure final DataGrid layout or GPU rendering. The next rendering callback is not proof that the frame has reached the screen. The existing background-priority `dispatcher_queue_ms` probe can starve without a continuous UI freeze. CPU/GC/render profiling is still needed to attribute those costs.
- `window_lag_s` is a signed difference to the actual analysis-window **end**, not the ten-second window length or FFT bin selection. Read it with both source states and bounds; a cycle can end without a new visible projection if results are unavailable. `review_custom_frequency_apply_perf` marks a projection that passed current-window checks and was applied.

Connection and cancellation timings are aggregated once per owned request/window. Desktop diagnostics are emitted at window/response/series-update boundaries, not per EEG sample, per SQL statement or per table cell. Use the existing log rotation and disable the desktop switch after diagnosis; diagnostic logging still adds some workload.

