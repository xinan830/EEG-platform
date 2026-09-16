# Unified Dynamic Analysis Frame Design

**Status:** proposed for implementation review  
**Date:** 2026-09-16  
**OpenSpec change:** `replace-algorithm-runtime-and-theta-beta-v2`

## 1. Problem and decision

The platform already shares some scientific contracts (units, range validation,
provenance presentation, and result formatting), but it does **not** yet have a
single producer for dynamic-analysis results.  User-defined Definition metrics
and official composite algorithms currently schedule dynamic windows, attach
quality, serialize points, and construct cache identities through different
paths.  That split caused real defects: a playback result could be produced
under an older result shape, reused from cache, and then appear empty or lack
debug evidence in a newer UI.

This design replaces that split with one backend-owned dynamic frame contract.
It does not change any EEG mathematics.

## 2. Non-negotiable constraints

- `offline-spectral-v3` PSD, Welch settings, band integration, IAPF fitting,
  Theta/Beta formulas, units, and quality thresholds retain their existing
  mathematical meaning.
- Raw EEG remains read-only.  Failed or gated values are `null`, never zero.
- The frontend never calculates EEG, PSD, band power, quality, or an algorithm
  formula.  It only requests, renders, and formats backend results.
- A dynamic point is anchored at the **end** of its analysis range.  Its
  `time_s` is always the actual `window_end_s`.
- A partial warm-up point is permitted once four seconds of clean source data
  are available only when the selected algorithm's dynamic policy permits it.
  A configured trailing window is used once enough data exist. This is
  scheduling semantics, not a different PSD algorithm.
- Every returned point has a stable result-contract version.  Cache identity
  must include that version whenever the point shape or evidence meaning changes.

## 3. Target responsibility boundary

```text
Playback / requested time
        |
        v
DynamicFramePlanner
        |
        v
DynamicAnalysisFrame
  (actual range, warm-up, source quality, spectral evidence, provenance)
        |
        +--> User-definition adapter  --> MetricOutput
        |
        +--> Official-IAPF adapter    --> MetricOutput
        |
        +--> Official-Theta/Beta adapter --> MetricOutput
        |
        v
Shared Run serializer, cache identity, artifact writer, provenance API
        |
        v
Frontend renderer and algorithm debug panel
```

Only `DynamicFramePlanner` may choose a dynamic window.  Only the frame builder
may load or attach PSD/quality evidence.  Algorithm adapters receive a complete
frame and may compute their own metric only; they may not construct a second
window, independently reload a spectrum, or invent a separate warm-up rule.

Each module declares one `DynamicAnalysisPolicy`: minimum window, permitted
window sizes, refresh step, and whether short warm-up output is scientifically
reportable. Run validation, the planner, catalog API, and frontend consume this
single policy. A generic 4 s Welch minimum does not make every algorithm's
output reportable after four seconds.

## 4. Core contracts

### DynamicFrameRequest

The normalized request contains the recording identity, requested playback end
time, configured analysis-window duration, refresh step, selected channel or
explicit algorithm channel roles, and analysis settings.  Validation occurs
once before the frame is planned.

### DynamicAnalysisFrame

Each frame contains:

```text
result_contract_version
time_s                    # equal to actual window_end_s
window_start_s
window_end_s
requested_window_duration_s
actual_window_duration_s
warmup
source_quality            # clean/total segments and gate reason
spectral_evidence         # PSD contract, frequency bins, units, Welch, filter,
                          # reference, sample rate, quality and provenance
failure                   # structured failure or null
```

The frame is valid even when the metric is unavailable.  In that case the
structured failure and `null` metric value describe the outcome; no consumer
must infer missing data from a missing field.

### MetricOutput

Every adapter returns a small typed result:

```text
value | null
unit
label
quality
failure | null
algorithm_evidence
```

For multi-role official Theta/Beta, one frame produces explicit role outputs
(`Fz`, `Pz`, `Oz`) without frontend averaging.  The selected display role is a
presentation choice, not a second computation.

## 5. Dynamic scheduling semantics

For a requested analysis duration of 10 s and a 1 s refresh step, a catch-up
request must preserve every valid refresh endpoint.  It must not collapse
multiple warm-up frames into one final short-range frame: doing so makes a
chart connect distant points and falsely suggest a constant value.
Partial warm-up windows are valid only at the beginning of a recording.  A
later catch-up request begins at a trailing-window boundary and must plan only
complete trailing windows; it must never reinterpret that boundary as a new
warm-up origin.

| Playback endpoint | Actual range | Warm-up | Point time |
| --- | --- | --- | --- |
| `< 4 s` | no frame | n/a | no point |
| `4 s` | `0–4 s` | yes | `4 s` |
| `5 s` | `0–5 s` | yes | `5 s` |
| `6 s` | `0–6 s` | yes | `6 s` |
| `8 s` | `0–8 s` | yes | `8 s` |
| `9 s` | `0–9 s` | yes | `9 s` |
| `10 s` | `0–10 s` | no | `10 s` |
| `26 s` | `16–26 s` | no | `26 s` |

The configured duration is never silently displayed as the actual duration.
Both are retained in the frame and debug panel.  Static analysis remains a
single explicitly requested range and does not use playback scheduling.

### IAPF policy

Official IAPF does not expose a dynamic warm-up candidate as a current IAPF.
Its dynamic policy is a fixed 30 s trailing window with a 5 s refresh step.
Before 30 s it returns no Hz point; at 30 s it produces `0–30 s`, then
`5–35 s`, `10–40 s`, and so on. This does not change PSD mathematics; it
prevents short-window peak bins from being presented as a stable individual
alpha peak frequency.

## 6. Migration sequence

1. Add the planner, frame, shared spectral-evidence builder, serializer, and
   contract-versioned cache identity with characterization tests.
2. Move official IAPF and official Theta/Beta adapters to consume frames.
3. Move Definition graph dynamic execution to consume the same frames.
4. Route all dynamic Run creation through the normalized request/service layer;
   retain algorithm-specific configuration only as adapter input.
5. Make the frontend use one dynamic-result contract and one provenance view;
   remove algorithm-family-specific assumptions from charts and debug dialogs.
6. Delete the duplicated dynamic window/evidence/serialization helpers only
   after point-by-point parity tests pass.

The migration is complete only when there is no active alternate producer of
dynamic windows, quality evidence, or result-point serialization.

## 7. Verification and safety gates

- Characterization tests compare old and new results for fixed inputs at each
  point after the first valid frame; allowable differences are only metadata
  additions and the explicitly corrected endpoint alignment.
- Tests cover 0–4 s, 4–10 s warm-up, exact full-window boundary, long playback,
  replay from zero, catch-up from zero through a full-window boundary, static
  requests, gates, missing channels, and cache reuse.
- IAPF and Theta/Beta retain their current role/mapping rules and return `null`
  with structured reasons when inputs are insufficient.
- API/provenance tests require every dynamic point to expose sample rate,
  preprocessing, Welch/frequency information when its source is spectral.
- Backend pytest, frontend type/Vitest/build, OpenSpec strict validation, and
  `git diff --check` are required before the change is considered complete.

## 8. Explicit non-goals

- No new EEG algorithm, frequency band, clinical conclusion, or frontend
  scientific computation.
- No implicit mapping from a channel position or alias to a logical electrode.
- No change to historical completed Runs; new result-contract versions merely
  prevent them being reused as if they had the new evidence contract.
