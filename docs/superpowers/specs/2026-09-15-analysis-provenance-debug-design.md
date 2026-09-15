# Analysis Provenance and Algorithm Debug Design

## Purpose

Make every analysis result explainable through one immutable backend-owned
provenance record and one reusable frontend presentation. All algorithm debug
views share the same base sections; an algorithm may append a dedicated section
only when its backend evidence supplies that domain-specific data.

This design does not change EEG mathematics, units, channel ordering, quality
rules, Welch semantics, reference semantics, or the existing result values.

## Problem

The current frontend has multiple independently maintained descriptions of
analysis fields. The algorithm debug dialog, PSD validation dialog and
spectrogram validation dialog each name and format overlapping fields. The
algorithm debug dialog also derives the Welch step from segment length and
overlap while other result paths hard-code the same sentence.

Scientific evidence must not depend on independently maintained frontend text
or frontend-derived calculation parameters.

## Backend Contract

Add an additive `analysis_provenance` object to Run result responses and any
result-view response that includes a Run. Its values are created by the backend
from the persisted Run plus the completed analysis evidence.

```json
{
  "analysis_provenance": {
    "contract_version": "analysis-provenance-v1",
    "definition_version": "1.0.0",
    "scientific_algorithm_version": "offline-spectral-v3",
    "implementation_version": "build-abc",
    "config_sha256": "4111546E8CDB",
    "mode": "dynamic",
    "requested_range": { "start_s": 12.0, "end_s": 22.0 },
    "actual_range": { "start_s": 12.0, "end_s": 22.0 },
    "channel": "F3",
    "channel_mapping": {},
    "analysis_reference": "original_recording_no_software_rereference",
    "sfreq_hz": 500,
    "filter": { "low_hz": 1.0, "high_hz": 30.0, "phase": "zero_phase" },
    "welch": {
      "segment_s": 4.0,
      "window": "hann",
      "overlap_fraction": 0.5,
      "step_s": 2.0
    },
    "frequency": { "low_hz": 1.0, "high_hz": 30.0, "point_count": 117 },
    "quality": { "status": "clean", "clean_segments": 4, "total_segments": 4, "reasons": [] }
  }
}
```

Rules:

- `step_s` is returned explicitly by the backend; the frontend does not derive
  it from Welch fields.
- `actual_range` is the exact data range for that immutable completed Run.
- A dynamic playback position is UI context, not provenance, and is displayed
  beside but never inside `actual_range`.
- Missing evidence fields are omitted or `null`, never invented as zero or a
  guessed default.
- Existing Run response fields stay available for API compatibility.

## Frontend Structure

Create a reusable `AnalysisProvenancePanel` that accepts only the normalized
backend provenance object. It owns Chinese labels, display order, unit formatting
and status visual language; it does not calculate scientific values.

```text
AlgorithmMetricDebugDialog
├─ AnalysisProvenancePanel             # every Run
├─ SpectralEvidencePanel               # only when backend returns PSD evidence
├─ MetricInputOutputPanel               # only when backend returns metric inputs/output
└─ AlgorithmSpecificEvidencePanel[]    # selected by evidence extension kind
```

The reusable base panel contains:

1. analysis mode and requested/actual range;
2. definition/scientific/implementation versions and configuration digest;
3. channel, mapping, analysis reference and sampling rate;
4. filter and Welch contract;
5. frequency coverage and quality gate.

Raw PSD points, full resolved configuration and artifact lists remain available
through explicit expandable sections. They are not discarded, but are not
rendered as hundreds of rows by default.

## Algorithm Extensions

The frontend uses a finite registry of evidence extension kinds. It never
inspects algorithm names, arbitrary graph nodes, or positional channels to guess
an extension.

Initial kinds:

```text
metric_inputs_output
spectral_band_power
faa_pairing
iapf_peak_cog
brainbeat_realtime
theta_beta_individualized_bands
```

Each extension has a documented backend schema and an isolated display component.
An algorithm whose Run has no extension still receives the complete base panel.

## Dynamic Behavior

For a dynamic algorithm, each completed Run carries its own immutable
provenance. The debug workbench follows the latest completed Run for the selected
algorithm. It separately shows current waveform playback position and the Run's
actual analysis range. A queued replacement Run leaves the prior completed
evidence visible; a completed or gate-failed replacement replaces it.

## Error Handling

- A missing provenance object is rendered as an explicit unavailable section;
  legacy fields may be shown only through a clearly labeled compatibility adapter.
- `gate_failed` retains quality and provenance evidence, with numerical outputs
  displayed as unavailable rather than zero.
- The frontend never invokes raw EEG, PSD or formula calculation to fill a
  missing field.

## Testing

- Backend serializer tests prove `analysis_provenance` contains exact persisted
  Run identity and explicit Welch `step_s`.
- Dynamic and static Run tests prove actual ranges, quality and null handling
  remain unchanged.
- Frontend component tests prove the base panel uses backend values, renders no
  speculative defaults, and does not calculate Welch step.
- Extension tests prove an extension appears only for its explicit evidence kind.
- Existing PSD/spectrogram/metric golden tests remain unchanged.

## Out of Scope

- changing the mathematical definition of any official or user algorithm;
- turning frontend formatters into signal-processing code;
- rendering every raw array expanded by default;
- clinical conclusion generation.
