# Change: Add desktop live filter bridge

Desktop high-pass and low-pass display controls currently do not affect the
live stream. Add a local Python-owned, stateful filter session so the WPF
client displays backend-filtered batches while raw V/float64 acquisition files
remain unchanged.
