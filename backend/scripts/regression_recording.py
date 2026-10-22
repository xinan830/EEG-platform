"""Fixed real-recording scenarios through isolated current production Run services."""

import copy
import json
from pathlib import Path

import numpy as np

from app.models.run import RunCreateRequest
from app.services.analysis_provenance import build_analysis_provenance
from app.services.recording_samples import WpfSampleReader
from app.services.recordings import RecordingService
from app.services.runs import RunService
from scripts import recording_audit_reference as ref
from scripts.regression_store import Differences, checksum


ALGORITHMS = ("psd", "stft", "peak_frequency", "iapf", "band_ratio",
              "theta_beta", "rbp", "faa", "brainbeat")
DURATION_S = 30


def source_checksums(directory):
    """Hash every original file; expose ordinal identifiers, never personal names."""
    paths = sorted(path for path in directory.rglob("*") if path.is_file())
    return {f"file-{index:03d}": {"sha256": checksum(path), "bytes": path.stat().st_size}
            for index, path in enumerate(paths)}


def source_setup(directory: Path):
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    profile = json.loads(manifest["HardwareConfiguration"]["montage_configuration_snapshot_json"])
    rows = sorted(profile["DerivedChannels"], key=lambda item: item["DisplayOrder"])
    labels = [item["PositiveLabel"] for item in rows]
    if len(set(labels)) != len(labels) or not {"F3", "F4", "Fz", "Pz"}.issubset(labels):
        raise ValueError("Recording must provide unique F3/F4/Fz/Pz acquisition outputs")
    if any(item["NegativeKind"] != 0 or item.get("NegativeLabels") for item in rows):
        raise ValueError("This fixed baseline requires acquisition REF outputs")
    alternate = copy.deepcopy(profile)
    alternate.update(Id="regression-average", Name="Regression average", Revision=1,
                     AverageReferenceLabels=labels)
    alternate["DerivedChannels"] = [dict(item, Name=f"{item['PositiveLabel']}-AVG",
                                        NegativeKind=2, NegativeLabels=labels) for item in rows]
    raw, names, fs, gaps = ref.read_source(directory)
    if len(raw) < round(DURATION_S * fs) or gaps or not np.isfinite(raw).all():
        raise ValueError("Fixed baseline requires >=30 seconds of finite contiguous samples")
    reader = WpfSampleReader(directory)
    try:
        if list(reader.names) != names or reader.sfreq != fs:
            raise ValueError("Independent reader channel/rate mismatch")
        np.testing.assert_array_equal(reader.read(0, reader.sample_count), raw)
    finally:
        reader.close()
    projected = raw[:, [names.index(label) for label in labels]]
    sources = {"REF": projected, "AVG": projected - projected.mean(axis=1, keepdims=True)}
    return alternate, labels, fs, sources


def scenario_config(identifier, mode, montage):
    roles = ["F3", "F4"] if identifier == "faa" else ["Fz", "Pz"] if identifier == "brainbeat" else ["Fz"]
    outputs = [next(output for output in montage["outputs"] if output["role"] == role) for role in roles]
    config = dict(algorithm_id=identifier, time=dict(start_s=0, end_s=DURATION_S),
                  channel=outputs[0]["id"], mode=mode, dynamic_window_s=20 if identifier == "faa" else 5,
                  refresh_step_s=1, analysis_montage_id=montage["id"],
                  analysis_montage_revision=montage["revision"],
                  analysis_montage_fingerprint=montage["fingerprint"],
                  analysis_montage_outputs=[dict(role=role, output=out["id"], unit="V")
                                           for role, out in zip(roles, outputs)])
    if identifier in {"psd", "stft", "faa"}:
        config.update(low_hz=1, high_hz=50, notch_hz=50)
    if identifier == "peak_frequency":
        config.update(low_hz=8, high_hz=13)
    if identifier == "band_ratio":
        config.update(numerator_low_hz=4, numerator_high_hz=8,
                      denominator_low_hz=13, denominator_high_hz=30)
    if len(outputs) == 2:
        config["f4_channel" if identifier == "faa" else "secondary_channel"] = outputs[1]["id"]
    return config


def normalize(value, aliases):
    """Replace known allocation identities only; keep scientific metadata intact."""
    if isinstance(value, dict):
        return {key: "selected-montage" if key == "montage_label" else normalize(item, aliases)
                for key, item in value.items()}
    if isinstance(value, list):
        return [normalize(item, aliases) for item in value]
    if isinstance(value, str):
        return aliases.get(value, value)
    return value


def snapshot_run(service, run, config, aliases):
    if run.status.value not in {"completed", "gate_failed"} or run.result_summary is None:
        raise ValueError(f"{config['algorithm_id']}: Run did not produce a scientific result")
    result = copy.deepcopy(run.result_summary)
    result.pop("artifacts", None)
    arrays = {}
    for artifact in service.list_artifacts(run.run_id):
        service.artifacts.verify(artifact)
        for key, array in service.artifacts.read_npz(artifact).items():
            if key == "window_evidence_json":
                result["window_evidence"] = json.loads(str(array.item()))
            elif key in arrays:
                raise ValueError("Duplicate artifact array")
            else:
                arrays[key] = array
    replacements = aliases | {run.run_id: "run", run.definition_id: "definition",
                               run.cache_key: "scientific-cache", run.config_sha256: "config-digest"}
    value = dict(status=run.status.value, config=run.config, result=result,
                 actual_range=run.actual_range, requested_range=run.requested_range,
                 channels=run.channel_mapping, reference=run.reference, filters=run.filters,
                 window=run.window, quality_rules=run.quality_rules,
                 provenance=build_analysis_provenance(run))
    return normalize(value, replacements), arrays


def replay(directory: Path, temporary: Path, progress=print):
    """Read originals, execute 36 current Runs in disposable DB/artifact storage."""
    from scripts.regression_reference import validate_scenario

    before = source_checksums(directory)
    alternate, labels, fs, sources = source_setup(directory)
    recordings = RecordingService(temporary / "recordings", temporary / "db.sqlite3")
    recording = recordings.register_wpf_recording(str(directory), signal_mode="acquisition_montage",
                                                 analysis_montage_profiles=[alternate])
    service = RunService(recordings, recordings.database_path, temporary / "artifacts")
    snapshots, arrays, checks, cache = {}, {}, Differences(), {}
    try:
        for kind in ("REF", "AVG"):
            montage = next(item for item in recording.montage_catalogue
                           if item["is_acquisition_default"] == (kind == "REF"))
            reader = recordings.open_sample_reader(recording, analysis_montage_id=montage["id"])
            try:
                checks.exact(f"{kind}.channels", list(reader.names), [f"{label}-{kind}" for label in labels])
                checks.numeric(f"{kind}.source_v", reader.read(0, reader.sample_count), sources[kind],
                               rtol=0, atol=1e-18)
            finally:
                reader.close()
            aliases = {recording.id: "recording", montage["id"]: f"montage-{kind}"}
            for identifier in ALGORITHMS:
                for mode in ("static", "dynamic"):
                    key = f"{kind}/{identifier}/{mode}"
                    progress(key)
                    config = scenario_config(identifier, mode, montage)
                    run = service.create(RunCreateRequest(recording_id=recording.id,
                                         analysis_type="official_algorithm", config=config))
                    snapshot, values = snapshot_run(service, run, config, aliases)
                    snapshots[key] = snapshot
                    arrays.update({f"{key}/{name}": value for name, value in values.items()})
                    validate_scenario(key, snapshot, values, sources[kind], labels, fs, checks, cache)
    finally:
        recordings._analysis_preprocess_cache.clear_recording(recording.id)
        after = source_checksums(directory)
        if before != after:
            raise ValueError("Original recording files changed during replay")
    source = dict(files=before, sfreq_hz=fs, sample_count=len(sources["REF"]),
                  channels=labels, unit="V", dtype="float64", sample_major=True)
    return dict(source=source, scenarios=snapshots), arrays, checks.report()
