# TNK WPF workbench boundary review

As of 2026-10-25, the single public `tnk_biofeedback` entry retains independent
backend child calculators. `TnkDetailView` follows the ANT workbench layout:
large EEG at top left, chart-family tabs below, playback/score/quality
at right. Record, montage and execution configuration is collapsible above it.

## Charts and ownership

- All waveform and metric plots are native SciChart. Eight tabs cover meditation,
  BrainBeat + Flat, fatigue, HAI, RBP, aperiodic exponent, FAA and IAPF.
- RBP trends show a stacked area per source channel, ordered delta/theta/alpha/beta
  with the ANT red/yellow/green/blue palette. Backend ratios are formatted as
  percentages without normalization; nulls remain gaps. Other metrics use time
  curves. A single returned window is a marked point at its actual end time,
  never a categorical column or an invented flat history.
- `TnkWindowChartHistory` keeps up to 600 returned on-demand windows per
  scientific session, replaces duplicates, sorts by time and inserts null gaps
  across uncomputed intervals. Source/role/session changes clear that history.
- `TnkResultChartProjection` reads immutable Run/current-window evidence.
  `TnkWorkbenchPresentation` only groups and formats returned values.
- `TnkAnalysisPlotView` provides zoom, pan, reset, series visibility, rollover
  values and Ctrl-click seek. Stacked bands remain visible to preserve composition.
- Chart cursor values use actual recording seconds and declared units. No EEG,
  frequency, fit or band-power calculation runs in WPF. The compact cortical
  value identifies its returned role; it is not relabelled as pooled Pz+Oz.

## Reusable waveform

`Shared/Waveform/SignalWaveformView` owns immutable display data, shared review
plot/label geometry, fixed µV spacing, a 100 µV scale, time formatting and seeks.
It owns no HTTP, TNK calculations or playback timer and can be reused elsewhere.

POST `/api/recordings/{id}/signal-preview` binds a montage snapshot and explicit
display low/high/notch. It reuses canonical continuous zero-phase preparation
before cropping a response of at most 30 seconds / two million sample-values.
Preparation may read the complete source into mapped buffers, guarded by a
512 MiB allocation budget and disk-space checks; the response limit is not a
claim that preparation reads only 30 seconds. Large sources may be rejected
with `SIGNAL_PREVIEW_RESOURCE_EXCEEDED`; no cropped-window filter substitute is used.

Responses declare µV, absolute seconds, channel order, filter settings and
display stride. Stride sampling can miss short peaks and is not analysis input.
The TNK scientific path remains fixed 1–30 Hz independently of display settings.
Raw recordings are not modified. Revision/cancellation/source checks reject
obsolete responses; mapping/montage changes withhold previous-source results.

## Playback and state duration

The 33 ms display clock is separate from one-second on-demand targets. Drag
release/Ctrl-click seek through the existing latest-only analysis coordinator.
The catalog's own playback timer is paused while the workbench clock owns playback.
Previous/next-window actions rebase the playback clock to the clamped target;
slider dragging pauses playback before releasing a new analysis target.

The user explicitly removed event annotations from the workbench. Event buttons,
lists, commands, request contracts and activation-time event requests were removed.
The existing recording-event backend remains owned by its original consumers.
GET `/api/runs/{id}/tnk-workbench?cursor_s=...` returns a typed cumulative score
timeline from a completed TNK Run. ANT source thresholds 55/65/75 and colors
define presentation states, not medical conclusions. Durations use recording
time; invalid windows break accumulation. WPF reads once per immutable Run
and selects returned cumulative frames during playback. An isolated on-demand
window does not claim whole-recording statistics.

Original ANT event-period reports are outside this algorithm workbench scope.
The shared 1–30 Hz TNK definitions and existing nine platform algorithms remain
unchanged.

## Verification

An isolated WPF harness uses the real saved 149.982-second Run and actual
continuously filtered waveform: 12 children, 11 with finite results, FAA
unavailable; all eight tab bindings are exercised. O2 is the explicitly
authorized Oz-role proxy, never a physical Oz measurement. Rendered images
are kept outside Git in `%TEMP%/brain-platform-tnk-validation/report/`.
The complete `AlgorithmDetailView` was rendered with application resource
dictionaries as well as the inner workbench. Final focused checks: 34 backend
tests in the previous iteration; this correction reran 16 backend preview/summary/size
checks and 423 desktop algorithm tests, all passing. One real returned window and
sixteen accumulated returned windows were separately rendered to verify points,
RBP stacks, metric curves and absence of event controls. WPF compiled and all eight tab
bindings rendered, with OpenSpec strict validation and diff checks passing.
Backend API/filter/summary tests and desktop projection/payload tests cover
the changed contracts. Actual desktop mouse interactions, end-to-end playback
smoothness and user acceptance remain task 5.4. Independent real-data numerical
references remain task 5.2; visual rendering is not scientific validation.
