# Analysis Foundation Unification Design

## Purpose

Centralize three cross-cutting concerns without changing EEG mathematics,
offline/dynamic time semantics, old routes, or user-visible scientific values:

1. analysis input validation and structured error ownership;
2. frontend API request behavior;
3. unit and number display formatting.

## Scope and Non-goals

This change applies to existing recording spectrum, spectrogram, and user
definition-metric flows. It does not make every quick PSD/spectrogram request
an asynchronous `AnalysisRun`; live viewer interactions keep their current
compatible routes and response timing. It does not add client-side scientific
calculation, unit conversion, authentication, plugins, or clinical conclusions.

## Rule Ownership

Validation is divided by what the validator can truthfully know:

```text
Frontend form constraints
  → immediate input affordances only; never authoritative

Pydantic request schemas
  → self-contained shape/range rules: finite number, start < end,
    minimum duration, custom frequency low < high, allowed mode values,
    nonempty and unique channels

Recording/analysis services
  → data-dependent rules: recording exists, requested interval fits its real
    duration, requested channels exist, sampling rate is finite and > 0, and
    requested spectral frequencies are below Nyquist
```

`AnalysisTimeRange`, `AnalysisFrequencyRange`, and `DefinitionMetricConfig`
remain the public request schemas. A new focused backend validation helper may
be used by recording/spectral services, but it must return structured domain
errors and must not duplicate formulas in API routes.

The browser never decides that an analysis is scientifically valid. It submits
the requested configuration, then renders the backend response or its stable
error code/message.

## Frontend Request Boundary

`frontend/src/api/client.ts` is the only browser transport boundary. The
existing `request<T>(path, init)` owns API base URL selection, JSON headers,
JSON parsing, `ApiRequestError`, stable backend code/message, and request ID.

`spectrum.ts` and `spectrogram.ts` migrate their four existing calls to that
helper. Their exported function names, request bodies, routes, and typed
response values do not change. No Axios dependency is introduced. Request
timeouts are deliberately not invented in the browser because cancellation and
long-running Run semantics are separate product decisions; errors are instead
reported consistently through `ApiRequestError`.

## Unit Display Boundary

The backend remains the scientific unit authority:

```text
raw EEG internal storage       V / float64
viewer presentation            µV (existing waveform contract)
PSD API numeric values         uV^2/Hz
absolute band power            uV^2
relative band power            ratio
spectrogram logarithmic value  dB re 1 uV^2/Hz
```

The frontend adds `unitDisplay(unit)` and `formatScientificValue(value, unit,
options)` as display-only utilities. Their canonical visible labels are:

```text
V                    V
uV                   µV
V^2                  V²
uV^2                 µV²
V^2/Hz               V²/Hz
uV^2/Hz              µV²/Hz
Hz                   Hz
s                    s
ratio                比值
percent              %
dimensionless        无量纲
dB re 1 uV^2/Hz      dB re 1 µV²/Hz
```

The utility does not multiply, divide, integrate, log-transform, normalize,
or infer a unit. Unknown values are displayed exactly as received, preserving
visibility of API contract mistakes rather than silently guessing.

## Page Migration

First migrate the highest-risk scientific text:

- algorithm debug provenance panel and metric extensions;
- PSD chart axis/tooltip and spectrum algorithm validation dialog;
- spectrogram chart tooltip/header;
- definition metric result cards and trend-axis labels.

Data values remain the backend values currently passed to those components.
The migration replaces label strings only; it must not change array values,
chart data, time axes, quality semantics, or frequency bounds.

## Test Strategy

- Backend tests cover self-contained validation and real-recording duration,
  channel, sampling-rate, and Nyquist failures with stable error codes.
- API client tests prove spectrum and spectrogram requests use the common
  `request()` error path and preserve POST bodies/query strings.
- Unit-format tests cover every canonical unit, an unknown passthrough, null,
  and non-finite numerical display.
- Existing PSD, spectrogram, Run, and definition-metric golden tests remain
  unchanged and must pass.
- Frontend typecheck, Vitest, backend pytest, production build, and
  `git diff --check` are required gates.

## Acceptance Criteria

- A configured PSD, configured spectrogram, and algorithm Run report errors
  through one frontend error class and present backend details unchanged.
- All migrated views use the same visible scientific unit labels.
- A rule requiring real EEG metadata is not duplicated in the browser.
- No EEG/PSD/metric output changes under existing golden fixtures.
