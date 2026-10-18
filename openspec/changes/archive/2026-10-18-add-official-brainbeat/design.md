# Design

Brainbeat is a composite spectral scalar. Each static or dynamic window loads
the Fz and Pz spectra, applies the existing PSD quality gate, estimates IAPF
from the Fz spectrum, and computes:

`relative_theta(Fz, IAPF) / relative_alpha(Pz, IAPF)`

Dynamic windows are independent. There is no cross-window EMA, warm-up state,
or state restoration. A rejected or unavailable source window remains null with
its backend failure and quality evidence.

The historical realtime EMA implementation remains under the shadow-validation
surface and is not imported by the runnable module.

The WPF module maps the primary selected channel to Fz and the existing
secondary channel selection to Pz. It displays backend scalar/window results
and never recomputes the ratio or IAPF.
