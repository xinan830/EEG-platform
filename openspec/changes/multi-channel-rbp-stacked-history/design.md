## Decision

The selected analysis montage is the only source of the multi-channel list.
Only outputs marked available in the recording's immutable montage snapshot are
accepted. Output order is the snapshot order and is part of the scientific
configuration identity.

RBP keeps the five fixed bands and fixed preprocessing. Each output channel is
computed independently from the same absolute window. Quality and failure are
per channel; a failed channel returns null band values while other channels in
the window may remain clean.

The public result carries `channel_order` and channel-indexed band values. The
on-demand payload carries the same order and one value per channel for each
band. The desktop history key is recording, montage identity, configuration
identity, channel, start sample, and end sample. A backward seek or any source
configuration change starts a new history generation.

The WPF view renders one stacked history chart per montage output. Each chart's
y-axis is ratio from 0 to 1 and its five bands are stacked at each window end
time. Missing or failed channel values are gaps, never zero substitutions.

## Compatibility

The scientific version advances from `official-rbp-v2` to `official-rbp-v3`.
Existing v2 artifacts remain immutable historical data and are not interpreted
as v3 multi-channel results. Requests without a montage continue to use the
single-channel v3 shape so older raw-recording workflows remain explicit.

