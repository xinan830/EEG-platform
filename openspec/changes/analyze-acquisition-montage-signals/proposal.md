# Analyze acquisition montage signals

WPF analysis currently lists every raw EEG input, even when acquisition saved a
two-output montage. Add an explicitly requested acquisition-montage analysis
view: output names and formulas come from immutable recording snapshots, never
current presets or the review display selection. Projection runs in the backend
before continuous preprocessing. Raw files, raw registration, old Runs and
scientific algorithm formulas remain unchanged. Missing or contradictory
snapshots fail with an explicit reason rather than falling back silently.

Non-goals: arbitrary live montage edits, rewriting recordings, and automatically
changing historical Runs when review display settings change.
