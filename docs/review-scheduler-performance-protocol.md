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

