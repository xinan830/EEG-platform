## Decision

`LiveMonitoringViewModel` now has a separate collection of derived montage
display rows. It is deliberately distinct from physical channel mapping rows:
physical rows identify source signals, while montage rows identify signals that
the waveform renderer can show after the selected rereference/combination.

The setting checkboxes only filter which derived traces the renderer creates in
the active session. They do not edit the saved montage, change its formula or
order, modify raw data, or change analysis inputs.
