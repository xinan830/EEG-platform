## Decision

The saved control is `PaperSpeedMillimetersPerSecond`, with supported values
5, 10, 15, 30, and 60 mm/s. At render time the canvas converts its WPF width
from device-independent pixels to nominal millimeters (`25.4 / 96`) and derives
the visible horizontal duration as `width_mm / speed_mm_per_s`.

Frame paging uses the nearest whole number of samples, while the x-axis retains
the derived continuous duration. The maximum rounding discrepancy is bounded by
half a sample period and does not advance the cursor beyond received samples.

## Resolution And Calibration

WPF layout units make the speed independent of pixel count and DPI scaling.
They cannot prove a physical ruler length when a monitor reports imperfect
physical dimensions or has custom OS scaling. Therefore this version calls the
result nominal physical paper speed and makes no clinical ruler-accuracy claim.

## Verification

Tests verify that 30 mm nominal canvas width at 30 mm/s yields one displayed
second, halving the speed doubles that duration, fractional windows retain their
x-axis duration, and the preference round-trips through local settings.
