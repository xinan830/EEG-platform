# Design

Reuse the existing completed-record registration, channel selection, range
validation, Run polling, and provenance display. The Run config includes
`algorithm_id=rbp`, the catalog scientific version, the selected channel,
`mode=static`, and exact requested seconds.

RBP returns a scalar metric with `output.value=null` and `band_values` containing
Delta, Theta, Alpha, and Beta ratios. WPF formats those backend values as a
compact result summary. It does not request a structured-preview artifact for
RBP and therefore does not show a misleading preview error. A failed or null
band value remains unavailable.

The existing chart visibility is algorithm-specific: PSD shows its frequency
curve, STFT shows its time-frequency bitmap, and RBP shows no scientific chart.
Switching algorithms clears all prior previews. Tests cover exact config,
static allow-list, scalar formatting, and PSD/STFT regression.
