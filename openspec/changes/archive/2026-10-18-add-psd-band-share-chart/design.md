# Design

The backend returns `band_share` in the fixed order Delta, Theta, Alpha, Beta,
Gamma with unit `ratio`. Static results use shape `[5]`; dynamic results use
shape `[window, 5]`. Band labels and ranges are included in calculation trace
evidence.

WPF parses the bounded structured preview and exposes `BandSharePoints` as
display data. A standalone `PsdBandShareChart` renders those points; it never
calculates or normalizes them.

The PSD detail page uses one chart selector for the spectrum and band-share
views. Both are independent SciChart components mounted in the same main plot
area, so switching views does not start a new Run or change the selected
channel or dynamic timeline. Unavailable bands are labeled, not shown as zero.

Adding `band_share` changes the persisted PSD result shape. The PSD manifest
uses a new implementation identity so a new Run cannot reuse an earlier cached
artifact that contains only `psd`. Existing Run artifacts remain unchanged.
