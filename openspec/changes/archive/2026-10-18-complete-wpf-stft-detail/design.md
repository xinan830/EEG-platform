# Design

The existing algorithm workspace owns recording selection, mode, Run creation,
and the dynamic timeline. The STFT-specific view model exposes only the
structured preview selected by that workspace. A dedicated WPF view displays a
bounded bitmap generated from backend `power_db`; color mapping is visual only.

Static output is `time x frequency`. Dynamic output is `window x inner_time x
frequency`. For the dynamic view, the selected complete window's matrix is
rendered using `window.start_s + time_center_s` as the absolute X coordinates.
The client must not concatenate or average windows. Rejected and unavailable
windows do not borrow an earlier image or synthesize zeros.

The parser validates channel count, axes, dimensions, and declared dB unit.
Null cells remain visually unavailable. The existing dynamic timeline controls
which window is visible; changing its cursor never submits another Run.

Validation covers parser shape/unit/null failures, static and dynamic window
selection, reset and rejected states, WPF compilation/tests, backend STFT
tests, and one actual recording Run. Visual acceptance remains a separate
manual step in WPF.
