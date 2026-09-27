# Design

Reuse the existing project/completed-record registration path and backend-declared raw channel list. Selecting PSD or STFT changes the view's presentation and clears the prior algorithm's preview. The submitted Run includes the selected algorithm ID/version, channel, `mode=static`, and the exact requested seconds. WPF checks finite in-record range; STFT additionally requires at least 4 seconds. The backend remains authoritative.

The structured-preview endpoint is requested with a fixed 100,000 numeric-cell ceiling. STFT parsing accepts only a static two-dimensional `power_db` array with time-major rows matching the returned time and frequency axes and explicit expected units. Null/nonfinite cells remain unavailable. The renderer maps returned dB values to colors without recomputing spectral values. It draws axes and a color legend. A rejected/oversized preview gets an explicit message; the saved Run remains completed.

Alternative rejected: thousands of WPF Rectangle elements per spectrogram. A bounded bitmap has lower layout cost. Another rejected alternative is calculating STFT or dB in C#, which would create a second scientific implementation.

Only backend services persist Runs and artifacts. Tests cover request payload, range validation, matrix orientation/shape, units, unavailable cells, preview ceiling, and PSD regression. Rollback removes only this additive WPF path.
