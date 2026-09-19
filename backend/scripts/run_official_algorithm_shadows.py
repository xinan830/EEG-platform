"""Run and persist redacted engineering shadow evidence for official metrics.

The script reads a local recording through the normal service and writes only
numeric comparison summaries to ``validation_runs``.  It never writes EEG
samples, source filenames, or patient-identifying fields to a report.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from scipy import signal

# Keep direct ``python scripts/run_...py`` invocation equivalent to ``-m``.
BACKEND_ROOT = Path(__file__).resolve().parents[1]
if str(BACKEND_ROOT) not in sys.path:
    sys.path.insert(0, str(BACKEND_ROOT))

from app.core.provenance import sha256_json
from app.eeg_core.faa import LEGACY_FAA_INITIAL_DISCARD_S
from app.eeg_core.official_algorithm_shadows import shadow_brainbeat, shadow_brainbeat_ema, shadow_faa, shadow_iapf, shadow_rbp, shadow_theta_beta
from app.eeg_core.official_definitions import ensure_official_definitions
from app.eeg_core.spectral import estimate_welch_psd, preprocess_offline
from app.models.run import ValidationCreateRequest
from app.services.definitions import DefinitionService
from app.services.recordings import RecordingService
from app.services.validations import ValidationService


def _persist(validation_service: ValidationService, *, kind: str, algorithm: str, identity: dict[str, object], config: dict[str, object], expected: list[float], actual: list[float], rtol: float = 1e-7, atol: float = 1e-9) -> dict[str, object]:
    result = validation_service.create(ValidationCreateRequest(
        kind=kind, algorithm_id=algorithm, algorithm_version="1.0.0", dataset_identity=identity,
        config_sha256=sha256_json(config), tolerances={"rtol": rtol, "atol": atol}, expected=expected, actual=actual,
    ))
    return result.model_dump(mode="json")


def _index(names: list[str], label: str) -> int:
    matches = {name.casefold(): index for index, name in enumerate(names)}
    if label.casefold() not in matches:
        raise ValueError(f"official shadow requires channel {label}")
    return matches[label.casefold()]


def run(recording_id: str, *, start_s: float = 0.0, window_s: float = 30.0, posterior_source: str | None = None) -> dict[str, object]:
    """Persist shadow parity for a single local recording without changing outputs."""
    recordings, validations = RecordingService(), ValidationService()
    ensure_official_definitions(DefinitionService())
    recording = recordings.require_recording(recording_id)
    raw, sfreq, names, _events = recordings.load_data(recording)
    values = np.asarray(raw, dtype=np.float64)
    filtered = preprocess_offline(values, sfreq)
    start = max(0, int(np.floor(start_s * sfreq)))
    stop = min(len(filtered), start + int(round(window_s * sfreq)))
    window = filtered[start:stop]
    if len(window) < int(round(4.0 * sfreq)):
        raise ValueError("shadow window requires at least four seconds")
    identity = {"recording_id": recording.id, "source_sha256": recording.source_sha256, "file_size_bytes": recording.file_size_bytes}
    config = {"actual_range": {"start_s": start / sfreq, "end_s": stop / sfreq}, "sfreq_hz": sfreq, "analysis_pipeline": "offline-spectral-v3", "reference": "original_recording_no_software_rereference"}
    reports: dict[str, object] = {}

    rbp_labels = [label for label in ("F3", "Fz", "Pz", "O2") if label.casefold() in {item.casefold() for item in names}]
    rbp_spectrum = estimate_welch_psd(window[:, [_index(names, label) for label in rbp_labels]], sfreq)
    if rbp_spectrum.gate_failed:
        reports["rbp"] = {"available": False, "reason": rbp_spectrum.gate_failed}
    else:
        rbp = shadow_rbp(rbp_spectrum.freqs, rbp_spectrum.psd, rbp_labels)
        expected = [value for row in rbp["bands"] for value in row["legacy"]]
        actual = [value for row in rbp["bands"] for value in row["primitive"]]
        reports["rbp"] = _persist(validations, kind="official_rbp_shadow", algorithm="rbp", identity=identity, config=config, expected=expected, actual=actual)

    iapf_labels = [label for label in ("Fz", "Pz", "O2") if label.casefold() in {item.casefold() for item in names}]
    iapf_spectrum = estimate_welch_psd(window[:, [_index(names, label) for label in iapf_labels]], sfreq)
    iapf = shadow_iapf(iapf_spectrum)
    if iapf["legacy"]["value"] is None or iapf["candidate"]["value"] is None:
        reports["iapf"] = {"available": False, "reason": iapf["legacy"]["gate_failed"], "shadow_passed": iapf["passed"]}
    else:
        reports["iapf"] = _persist(validations, kind="official_iapf_shadow", algorithm="iapf", identity=identity, config=config, expected=[iapf["legacy"]["value"]], actual=[iapf["candidate"]["value"]])

    # The third metric input is the logical ``Oz`` role, not automatically the
    # third available/occipital-looking channel.  A recording mapping or an
    # explicit CLI argument must supply the raw source label.
    mapped_posterior = posterior_source or (recording.mapping.oz if recording.mapping else None)
    theta_beta_labels = ["Fz", "Pz", mapped_posterior] if mapped_posterior else []
    available = {item.casefold() for item in names}
    if any(label.casefold() not in available for label in theta_beta_labels):
        theta_beta_labels = []
    if len(theta_beta_labels) != 3:
        reports["theta_beta"] = {"available": False, "reason": "logical_oz_mapping_required", "required_roles": ["Fz", "Pz", "Oz"]}
    else:
        theta_beta_spectrum = estimate_welch_psd(window[:, [_index(names, label) for label in theta_beta_labels]], sfreq)
        theta_beta = shadow_theta_beta(theta_beta_spectrum, float(iapf["legacy"]["value"] or 10.0), tuple(theta_beta_labels))
        if theta_beta["legacy_values"]:
            theta_config = {**config, "channel_mapping": {"Fz": "Fz", "Pz": "Pz", "Oz": mapped_posterior}, "iapf_hz": theta_beta["iapf_hz"], "formula": "theta(iapf-6..iapf-2)/beta(iapf+2..30)"}
            reports["theta_beta"] = _persist(validations, kind="official_theta_beta_shadow", algorithm="theta_beta", identity=identity, config=theta_config, expected=theta_beta["legacy_values"], actual=theta_beta["candidate_values"])
        else:
            reports["theta_beta"] = {"available": False, "reason": "low_quality_or_zero_beta", "shadow_passed": theta_beta["passed"]}

    frontal = filtered[int(round(LEGACY_FAA_INITIAL_DISCARD_S * sfreq)):, [_index(names, "F3"), _index(names, "F4")]]
    faa = shadow_faa(frontal[:, 0], frontal[:, 1], sfreq)
    if faa["legacy"]["faa"] is None or faa["candidate"]["faa"] is None:
        reports["faa"] = {"available": False, "reason": faa["legacy"]["reason"], "shadow_passed": faa["passed"]}
    else:
        faa_config = {**config, "scope": "full_recording_after_12_seconds", "paired_epoch_s": 2.0, "overlap": 0.5}
        reports["faa"] = _persist(validations, kind="official_faa_shadow", algorithm="faa", identity=identity, config=faa_config, expected=[faa["legacy"]["faa"]], actual=[faa["candidate"]["faa"]])

    frame_samples = int(round(2.0 * sfreq))
    frame_values: list[float] = []
    for frame_start in range(0, min(len(window), frame_samples * 4) - frame_samples + 1, frame_samples):
        frame = window[frame_start:frame_start + frame_samples]
        freqs, fz_psd = signal.welch(frame[:, _index(names, "Fz")], sfreq, nperseg=frame_samples, noverlap=frame_samples // 2, window="hann")
        _, pz_psd = signal.welch(frame[:, _index(names, "Pz")], sfreq, nperseg=frame_samples, noverlap=frame_samples // 2, window="hann")
        item = shadow_brainbeat(freqs, fz_psd, pz_psd, float(iapf["legacy"]["value"] or 10.0))
        frame_values.append(float(item["legacy"]))
    brainbeat = shadow_brainbeat_ema(frame_values, warmup_epochs=3, ema_alpha=0.15)
    ema_rows = [row for row in brainbeat["frames"] if row["legacy"] is not None]
    if not ema_rows:
        reports["brainbeat"] = {"available": False, "reason": "ema_warmup_incomplete"}
    else:
        brainbeat_config = {**config, "scope": "formula_and_log_ema_only", "welch_segment_s": 2.0, "welch_overlap": 0.5, "ema_alpha": 0.15, "warmup_epochs": 3, "limitation": "input uses offline-preprocessed local EEG; this is not causal-filter pipeline parity"}
        reports["brainbeat"] = _persist(validations, kind="official_brainbeat_shadow", algorithm="brainbeat", identity=identity, config=brainbeat_config, expected=[row["legacy"] for row in ema_rows], actual=[row["candidate"] for row in ema_rows])
    return {"scope": "engineering_shadow_only_not_clinical_validation", "recording_id": recording.id, "reports": reports}


def main() -> None:
    parser = argparse.ArgumentParser(description="Persist official algorithm shadow validation evidence")
    parser.add_argument("recording_id")
    parser.add_argument("--start-s", type=float, default=0.0)
    parser.add_argument("--window-s", type=float, default=30.0)
    parser.add_argument("--posterior-source", help="raw recording channel explicitly mapped to logical Oz for Theta/Beta validation")
    args = parser.parse_args()
    print(json.dumps(run(args.recording_id, start_s=args.start_s, window_s=args.window_s, posterior_source=args.posterior_source), ensure_ascii=True, sort_keys=True))


if __name__ == "__main__":
    main()
