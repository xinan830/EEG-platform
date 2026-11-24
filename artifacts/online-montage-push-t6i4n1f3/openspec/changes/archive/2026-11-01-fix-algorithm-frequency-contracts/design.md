# Design

Peak/ratio retain whole-recording zero-phase Butterworth 1-30 Hz without notch.
The frequency limits describe the supported analysis domain, not the hardware
Nyquist alone. Pydantic fields reject non-finite or out-of-domain values and
client-safe schemas advertise the same bounds in static/dynamic modes.

The shared snapshot factory accepts explicit filter low/high/notch arguments.
Defaults are the frozen 1-30 Hz contract. PSD/STFT pass their own validated
filter settings; peak/ratio use the frozen defaults and keep band limits only
in window evidence. RBP retains its explicit fixed 1-50 Hz/50 Hz override.
No concrete algorithm branches enter common Run services.

Peak computation additionally checks full axis coverage after no-bin handling;
an incomplete axis cannot yield clean output even with a valid configured band.
Old successful in-domain numerical output must remain identical. Implementation
identity changes invalidate inconsistent cached evidence without relabeling old
stored Runs. A separate scientific version would be needed to expand domains.

Verification: module config/schema parity, explicit notch variants, runtime
static/dynamic validation before signal loading, partial-axis rejection, Run
snapshot persistence, all backend tests and the same real-recording independent
numerical audit. New audit output is separate from the original evidence.
